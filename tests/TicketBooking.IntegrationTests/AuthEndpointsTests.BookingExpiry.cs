using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TicketBooking.Api.Booking;
using TicketBooking.Contracts.Booking;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Domain.Booking;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Payment;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Persistence;
using PaymentEntity = TicketBooking.Domain.Payment.Payment;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresTheory]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    public async Task BookingExpiry_ExactCapturedDeadlineDeterminesAtomicRelease(int ticks, int expired)
    {
        var setup = await PendingBooking();
        clock.Set(setup.Booking.PaymentDeadline.AddTicks(ticks));
        Assert.Equal(expired, (await BookingExpiryProcessor().ProcessBatchAsync()).Expired);
        await AssertBookingAllocation(setup.Booking, expired == 0 ? BookingStatus.PendingPayment : BookingStatus.Expired,
            expired == 0 ? SessionSeatStatus.Held : SessionSeatStatus.Available, expired == 0 ? 2 : 3);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var hold = await db.SeatHolds.Include(value => value.Items).SingleAsync();
        Assert.Equal(SeatHoldStatus.Converted, hold.Status);
        Assert.Equal(setup.Hold.ExpiresAt, hold.ExpiresAt);
        Assert.All(hold.Items, item => Assert.Equal(1, item.AllocationVersion));
        Assert.False(await scope.ServiceProvider.GetRequiredService<BookingExpiryReleaser>().ExpireAsync(Guid.NewGuid()));
    }

    [PostgresFact]
    public async Task BookingExpiry_OriginalHoldExpiryAndPaymentRetriesDoNotShortenOrExtendRetention()
    {
        var setup = await PendingBooking();
        Assert.Equal(setup.Booking.CreatedAt.AddMinutes(15), setup.Booking.PaymentDeadline);
        Assert.True(setup.Booking.PaymentDeadline > setup.Hold.ExpiresAt);
        clock.Set(setup.Hold.ExpiresAt);
        Assert.Equal(0, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        Assert.Equal(0, (await BookingExpiryProcessor().ProcessBatchAsync()).Expired);
        using var login = await Login("owner@example.test", Password);
        var token = (await login.Content.ReadFromJsonAsync<TicketBooking.Contracts.Auth.LoginResponse>())!.AccessToken;
        using var blocked = await AdminRequest(HttpMethod.Post, $"/api/sessions/{setup.Booking.SessionId}/holds", token,
            new CreateHoldRequest(setup.Booking.Items.Select(item => item.SessionSeatId).ToArray()));
        await AssertProblem(blocked, 409, "seat_not_available");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var booking = await db.Bookings.SingleAsync();
            var failed = PaymentEntity.CreateForBooking(Guid.NewGuid(), booking, "attempt-1", clock);
            failed.Fail("provider-failure");
            var retry = PaymentEntity.CreateForBooking(Guid.NewGuid(), booking, "attempt-2", clock);
            db.Payments.AddRange(failed, retry);
            await db.SaveChangesAsync();
        }
        clock.Set(setup.Booking.PaymentDeadline.AddTicks(-1));
        Assert.Equal(0, (await BookingExpiryProcessor().ProcessBatchAsync()).Expired);
        await AssertBookingAllocation(setup.Booking, BookingStatus.PendingPayment, SessionSeatStatus.Held, 2);
        clock.Set(setup.Booking.PaymentDeadline);
        Assert.Equal(1, (await BookingExpiryProcessor().ProcessBatchAsync()).Expired);
        await using var check = app.Services.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Equal(new[] { PaymentStatus.Failed, PaymentStatus.Pending },
            await database.Payments.OrderBy(value => value.IdempotencyKey).Select(value => value.Status).ToArrayAsync());
        Assert.Equal(setup.Booking.PaymentDeadline, (await database.Bookings.SingleAsync()).PaymentDeadline);
    }

    [PostgresTheory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task BookingExpiry_SessionStartDeterminesWhetherReleasedSeatsAreBookable(int ticks)
    {
        var setup = await PendingBooking();
        clock.Set(setup.Booking.SessionStartsAt.AddTicks(ticks));
        Assert.Equal(1, (await BookingExpiryProcessor().ProcessBatchAsync()).Expired);
        await AssertBookingAllocation(setup.Booking, BookingStatus.Expired,
            ticks < 0 ? SessionSeatStatus.Available : SessionSeatStatus.Unavailable, 3);
    }

    [PostgresTheory]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Expired)]
    public async Task BookingExpiry_TerminalBookingsAreNeverReleased(BookingStatus status)
    {
        var setup = await PendingBooking();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var booking = await db.Bookings.SingleAsync();
        db.Entry(booking).Property(value => value.Status).CurrentValue = status;
        await db.SaveChangesAsync();
        clock.Set(setup.Booking.PaymentDeadline);
        Assert.Equal(0, (await BookingExpiryProcessor().ProcessBatchAsync()).Examined);
        Assert.False(await scope.ServiceProvider.GetRequiredService<BookingExpiryReleaser>().ExpireAsync(booking.Id));
        await AssertBookingAllocation(setup.Booking, status, SessionSeatStatus.Held, 2);
    }

    [PostgresTheory]
    [InlineData(SessionStatus.Cancelled)]
    [InlineData(SessionStatus.Completed)]
    public async Task BookingExpiry_NonScheduledSessionReleasesToUnavailable(SessionStatus status)
    {
        var setup = await PendingBooking();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var session = await db.Sessions.SingleAsync();
        db.Entry(session).Property(value => value.Status).CurrentValue = status;
        await db.SaveChangesAsync();
        clock.Set(setup.Booking.PaymentDeadline);
        Assert.Equal(1, (await BookingExpiryProcessor().ProcessBatchAsync()).Expired);
        await AssertBookingAllocation(setup.Booking, BookingStatus.Expired, SessionSeatStatus.Unavailable, 3);
    }

    [PostgresFact]
    public async Task BookingExpiry_ConfirmedBeforeDeadlineKeepsBookedSeatsAfterDeadline()
    {
        var setup = await PendingBooking();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var booking = await db.Bookings.Include(value => value.Items).SingleAsync();
            booking.Confirm(clock);
            foreach (var item in booking.Items)
                await new SessionSeatAllocationWriter(db).ConfirmAsync(item.SessionSeatId, item.AllocationVersion!.Value);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        clock.Set(setup.Booking.PaymentDeadline);
        Assert.Equal(0, (await BookingExpiryProcessor().ProcessBatchAsync()).Expired);
        await AssertBookingAllocation(setup.Booking, BookingStatus.Confirmed, SessionSeatStatus.Booked, 3);
    }

    [PostgresTheory]
    [InlineData("legacy")]
    [InlineData("stale")]
    [InlineData("booked")]
    [InlineData("save")]
    [InlineData("commit")]
    public async Task BookingExpiry_FailureRollsBackTheWholeReleaseWithoutRefreshingTokens(string scenario)
    {
        var setup = await PendingBooking();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var item = await db.BookingItems.OrderByDescending(value => value.SessionSeatId).FirstAsync();
        if (scenario == "legacy")
        {
            db.Entry(item).Property(value => value.AllocationVersion).CurrentValue = null;
            await db.SaveChangesAsync();
        }
        else if (scenario == "stale")
            await new SessionSeatAllocationWriter(db).TransferToBookingAsync(item.SessionSeatId, 2);
        else if (scenario == "booked")
            await new SessionSeatAllocationWriter(db).ConfirmAsync(item.SessionSeatId, 2);
        else if (scenario == "save") holdWriteFailure.FailBookingExpiry = true;
        else cacheCommitFailure.FailNextCommit = true;
        var before = await db.SessionSeats.AsNoTracking().ToDictionaryAsync(value => value.Id, value => new { value.Status, value.Version });
        clock.Set(setup.Booking.PaymentDeadline);
        Assert.Equal(0, (await BookingExpiryProcessor().ProcessBatchAsync()).Expired);
        db.ChangeTracker.Clear();
        Assert.Equal(BookingStatus.PendingPayment, (await db.Bookings.SingleAsync()).Status);
        Assert.All(await db.SessionSeats.ToArrayAsync(), seat =>
        {
            Assert.Equal(before[seat.Id].Status, seat.Status);
            Assert.Equal(before[seat.Id].Version, seat.Version);
        });
        Assert.Equal(setup.Booking.PaymentDeadline, (await db.Bookings.SingleAsync()).PaymentDeadline);
    }

    [PostgresFact]
    public async Task BookingExpiry_ConcurrentWorkersReleaseOnceAndCannotReleaseAReacquiredHold()
    {
        var setup = await PendingBooking();
        clock.Set(setup.Booking.PaymentDeadline);
        firstHoldClaim.Enabled = true;
        var first = BookingExpiryProcessor().ProcessBatchAsync();
        try
        {
            await firstHoldClaim.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var competing = await BookingExpiryProcessor().ProcessBatchAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, competing.Expired);
            await AssertBookingAllocation(setup.Booking, BookingStatus.PendingPayment, SessionSeatStatus.Held, 2);
        }
        finally { firstHoldClaim.Continue.TrySetResult(); }
        Assert.Equal(1, (await first.WaitAsync(TimeSpan.FromSeconds(20))).Expired);
        using var login = await Login("owner@example.test", Password);
        var token = (await login.Content.ReadFromJsonAsync<TicketBooking.Contracts.Auth.LoginResponse>())!.AccessToken;
        using var acquired = await AdminRequest(HttpMethod.Post, $"/api/sessions/{setup.Booking.SessionId}/holds", token,
            new CreateHoldRequest(setup.Booking.Items.Select(item => item.SessionSeatId).ToArray()));
        Assert.Equal(HttpStatusCode.Created, acquired.StatusCode);
        Assert.Equal(0, (await BookingExpiryProcessor().ProcessBatchAsync()).Expired);
        await AssertBookingAllocation(setup.Booking, BookingStatus.Expired, SessionSeatStatus.Held, 4);
    }

    [PostgresFact]
    public async Task BookingExpiry_StaleTrackedConfirmationCannotOverwriteExpiredState()
    {
        var setup = await PendingBooking();
        await using var stale = app.Services.CreateAsyncScope();
        var db = stale.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var booking = await db.Bookings.SingleAsync();
        booking.Confirm(clock);
        clock.Set(setup.Booking.PaymentDeadline);
        Assert.Equal(1, (await BookingExpiryProcessor().ProcessBatchAsync()).Expired);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
        await AssertBookingAllocation(setup.Booking, BookingStatus.Expired, SessionSeatStatus.Available, 3);
    }

    [PostgresFact]
    public async Task BookingExpiry_SessionStartingDuringSaveRollsBackThenReleasesUnavailable()
    {
        var setup = await PendingBooking();
        clock.Set(setup.Booking.PaymentDeadline);
        holdWriteFailure.OnBookingExpirySaving = () =>
        {
            clock.Set(setup.Booking.SessionStartsAt);
            holdWriteFailure.OnBookingExpirySaving = null;
        };
        Assert.Equal(0, (await BookingExpiryProcessor().ProcessBatchAsync()).Expired);
        await AssertBookingAllocation(setup.Booking, BookingStatus.PendingPayment, SessionSeatStatus.Held, 2);
        Assert.Equal(1, (await BookingExpiryProcessor().ProcessBatchAsync()).Expired);
        await AssertBookingAllocation(setup.Booking, BookingStatus.Expired, SessionSeatStatus.Unavailable, 3);
    }

    [PostgresFact]
    public async Task BookingExpiry_BoundedCursorSkipsPoisonRowsWithoutStarvingLaterBookings()
    {
        clock.Set(new DateTimeOffset(clock.GetUtcNow().UtcTicks / 10 * 10, TimeSpan.Zero));
        var holds = await ExpiryHolds(3);
        var token = (await LoginToken()).AccessToken;
        foreach (var hold in holds)
        {
            using var response = await ConvertHold(hold.Id, token);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var bookings = await db.Bookings.Include(value => value.Items).OrderBy(value => value.Id).ToArrayAsync();
        db.Entry(bookings[0].Items[0]).Property(value => value.AllocationVersion).CurrentValue = null;
        await db.SaveChangesAsync();
        clock.Set(bookings[0].PaymentDeadline);
        var processor = BookingExpiryProcessor(1);
        var first = await processor.ProcessBatchAsync();
        Assert.Equal(1, first.Examined);
        Assert.Equal(0, first.Expired);
        var second = await processor.ProcessBatchAsync(first.NextCursor);
        Assert.Equal(1, second.Expired);
        var third = await processor.ProcessBatchAsync(second.NextCursor);
        Assert.Equal(1, third.Expired);
        var end = await processor.ProcessBatchAsync(third.NextCursor);
        Assert.Equal(0, end.Examined);
        Assert.Null(end.NextCursor);
        Assert.Equal(0, (await processor.ProcessBatchAsync()).Expired);
    }

    [PostgresFact]
    public async Task BookingExpiry_HostedServiceRunsImmediatelyAndStopsPromptly()
    {
        var setup = await PendingBooking();
        clock.Set(setup.Booking.PaymentDeadline);
        var log = new BookingExpiryBatchLog();
        using var worker = new BookingExpiryWorker(BookingExpiryProcessor(),
            new BookingExpiryOptions { PollInterval = TimeSpan.FromHours(1) }, clock, log);
        await worker.StartAsync(CancellationToken.None);
        try { await log.Completed.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await worker.StopAsync(timeout.Token);
        }
        await AssertBookingAllocation(setup.Booking, BookingStatus.Expired, SessionSeatStatus.Available, 3);
    }

    private BookingExpiryBatchProcessor BookingExpiryProcessor(int batchSize = 100) => new(
        app.Services.GetRequiredService<IServiceScopeFactory>(), clock, new BookingExpiryOptions { BatchSize = batchSize },
        NullLogger<BookingExpiryBatchProcessor>.Instance);

    private async Task<(BookingResponse Booking, HoldResponse Hold)> PendingBooking()
    {
        clock.Set(new DateTimeOffset(clock.GetUtcNow().UtcTicks / 10 * 10, TimeSpan.Zero));
        var setup = await OwnedHold();
        clock.Set(clock.GetUtcNow().AddMinutes(8));
        using var response = await ConvertHold(setup.Hold.Id, setup.Owner);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<BookingResponse>())!, setup.Hold);
    }

    private async Task AssertBookingAllocation(BookingResponse original, BookingStatus status, SessionSeatStatus seatStatus, long version)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var stored = await db.Bookings.Include(value => value.Items).SingleAsync(value => value.Id == original.Id);
        Assert.Equal(status, stored.Status);
        Assert.Equal(original.PaymentDeadline, stored.PaymentDeadline);
        Assert.Equal(original.TotalAmount, stored.TotalAmount.Amount);
        Assert.Equal(original.Currency, stored.TotalAmount.Currency);
        Assert.Equal(original.Items.Count, stored.Items.Count);
        foreach (var item in stored.Items)
        {
            var expected = original.Items.Single(value => value.SessionSeatId == item.SessionSeatId);
            Assert.Equal(expected.Price, item.PriceSnapshot.Amount);
            Assert.Equal(expected.Currency, item.PriceSnapshot.Currency);
            Assert.Equal(2, item.AllocationVersion);
        }
        Assert.All(await db.SessionSeats.Where(value => value.SessionId == original.SessionId).ToArrayAsync(), seat =>
        {
            Assert.Equal(seatStatus, seat.Status);
            Assert.Equal(version, seat.Version);
        });
    }

    private sealed class BookingExpiryBatchLog : ILogger<BookingExpiryWorker>
    {
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Name == "BookingExpiryBatchCompleted") Completed.TrySetResult();
        }
    }
}
