using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TicketBooking.Api.Reservation;
using TicketBooking.Contracts.Administration;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresTheory]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    public async Task HoldExpiry_ExactDeadlineDeterminesAtomicRelease(int ticks, int expectedExpired)
    {
        var holds = await ExpiryHolds(1, 2);
        var hold = holds[0];
        clock.Set(hold.ExpiresAt.AddTicks(ticks));
        var result = await ExpiryProcessor().ProcessBatchAsync();
        Assert.Equal(expectedExpired, result.Expired);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var stored = await db.SeatHolds.Include(value => value.Items).SingleAsync();
        Assert.Equal(expectedExpired == 0 ? SeatHoldStatus.Active : SeatHoldStatus.Expired, stored.Status);
        Assert.Equal(hold.ExpiresAt, stored.ExpiresAt);
        Assert.Equal(hold.TotalAmount, stored.TotalAmount.Amount);
        Assert.Equal(hold.Currency, stored.TotalAmount.Currency);
        Assert.Equal(2, stored.Items.Count);
        Assert.All(stored.Items, item => Assert.Equal(1, item.AllocationVersion));
        Assert.All(await db.SessionSeats.ToArrayAsync(), seat =>
        {
            Assert.Equal(expectedExpired == 0 ? SessionSeatStatus.Held : SessionSeatStatus.Available, seat.Status);
            Assert.Equal(expectedExpired == 0 ? 1 : 2, seat.Version);
        });
    }

    [PostgresTheory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task HoldExpiry_SessionStartDeterminesAvailableOrUnavailable(int ticks)
    {
        var holds = await ExpiryHolds(1);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var session = await db.Sessions.SingleAsync();
        clock.Set(session.StartsAt.AddTicks(ticks));
        Assert.Equal(1, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        var seat = await db.SessionSeats.SingleAsync(value => value.Id == holds[0].Items[0].SessionSeatId);
        Assert.Equal(ticks < 0 ? SessionSeatStatus.Available : SessionSeatStatus.Unavailable, seat.Status);
        Assert.Equal(2, seat.Version);
    }

    [PostgresFact]
    public async Task HoldExpiry_BoundedCursorBatchesProcessTiedDeadlinesAndWrap()
    {
        var holds = await ExpiryHolds(3);
        clock.Set(holds[0].ExpiresAt);
        var processor = ExpiryProcessor(1);
        HoldExpiryCursor? cursor = null;
        for (var index = 0; index < 3; index++)
        {
            var result = await processor.ProcessBatchAsync(cursor);
            Assert.Equal(1, result.Examined);
            Assert.Equal(1, result.Expired);
            Assert.NotNull(result.NextCursor);
            cursor = result.NextCursor;
        }
        var end = await processor.ProcessBatchAsync(cursor);
        Assert.Equal(0, end.Examined);
        Assert.Null(end.NextCursor);
        Assert.Equal(0, (await processor.ProcessBatchAsync()).Expired);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.All(await scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().SeatHolds.ToArrayAsync(),
            hold => Assert.Equal(SeatHoldStatus.Expired, hold.Status));
    }

    [PostgresFact]
    public async Task HoldExpiry_SkipsConvertedCancelledExpiredAndFutureHolds()
    {
        var holds = await ExpiryHolds(4);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var converted = await db.SeatHolds.SingleAsync(value => value.Id == holds[0].Id);
        converted.Convert(converted.UserId, clock);
        var cancelled = await db.SeatHolds.SingleAsync(value => value.Id == holds[1].Id);
        cancelled.Cancel(cancelled.UserId, clock);
        await db.SaveChangesAsync();
        clock.Set(holds[0].ExpiresAt);
        var expired = await db.SeatHolds.SingleAsync(value => value.Id == holds[2].Id);
        expired.Expire(clock);
        var future = await db.SeatHolds.SingleAsync(value => value.Id == holds[3].Id);
        db.Entry(future).Property(value => value.ExpiresAt).CurrentValue = clock.GetUtcNow().AddMinutes(1);
        await db.SaveChangesAsync();
        var result = await ExpiryProcessor().ProcessBatchAsync();
        Assert.Equal(0, result.Examined);
        Assert.Equal(0, result.Expired);
        Assert.All(await db.SessionSeats.AsNoTracking().Where(value => value.Status == SessionSeatStatus.Held).ToArrayAsync(),
            seat => Assert.Equal(1, seat.Version));
        Assert.Equal(4, await db.SessionSeats.CountAsync(value => value.Status == SessionSeatStatus.Held));
    }

    [PostgresTheory]
    [InlineData("legacy")]
    [InlineData("stale")]
    [InlineData("save_failure")]
    public async Task HoldExpiry_PoisonHoldRollsBackButDoesNotStarveLaterBatches(string scenario)
    {
        await ExpiryHolds(2, 2);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var ordered = await db.SeatHolds.Include(value => value.Items).OrderBy(value => value.Id).ToArrayAsync();
        var poison = ordered[0];
        var lastItem = poison.Items.OrderBy(item => item.SessionSeatId).Last();
        if (scenario == "legacy")
        {
            db.Entry(lastItem).Property(item => item.AllocationVersion).CurrentValue = null;
            await db.SaveChangesAsync();
        }
        else if (scenario == "stale")
            Assert.Equal(2, await new SessionSeatAllocationWriter(db).TransferToBookingAsync(lastItem.SessionSeatId, 1));
        else holdWriteFailure.FailingExpiryId = poison.Id;
        var before = await db.SessionSeats.AsNoTracking().Where(seat => seat.SessionId == poison.SessionId)
            .ToDictionaryAsync(seat => seat.Id, seat => new { seat.Status, seat.Version });
        clock.Set(poison.ExpiresAt);
        var processor = ExpiryProcessor(1);
        var first = await processor.ProcessBatchAsync();
        Assert.Equal(1, first.Examined);
        Assert.Equal(0, first.Expired);
        Assert.NotNull(first.NextCursor);
        var second = await processor.ProcessBatchAsync(first.NextCursor);
        Assert.Equal(1, second.Expired);
        db.ChangeTracker.Clear();
        Assert.Equal(SeatHoldStatus.Active, (await db.SeatHolds.SingleAsync(value => value.Id == poison.Id)).Status);
        Assert.Equal(SeatHoldStatus.Expired, (await db.SeatHolds.SingleAsync(value => value.Id == ordered[1].Id)).Status);
        Assert.All(await db.SessionSeats.Where(seat => seat.SessionId == poison.SessionId).ToArrayAsync(), seat =>
        {
            Assert.Equal(before[seat.Id].Status, seat.Status);
            Assert.Equal(before[seat.Id].Version, seat.Version);
        });
    }

    [PostgresFact]
    public async Task HoldExpiry_FailedHoldDoesNotRollbackOtherHoldsInTheSameBatch()
    {
        var holds = await ExpiryHolds(2);
        holdWriteFailure.FailingExpiryId = holds[0].Id;
        clock.Set(holds[0].ExpiresAt);
        var result = await ExpiryProcessor().ProcessBatchAsync();
        Assert.Equal(2, result.Examined);
        Assert.Equal(1, result.Expired);
    }

    [PostgresFact]
    public async Task HoldExpiry_SessionStartingDuringSaveRollsBackThenReleasesUnavailable()
    {
        var holds = await ExpiryHolds(1, 2);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var session = await db.Sessions.SingleAsync();
        clock.Set(holds[0].ExpiresAt);
        holdWriteFailure.OnExpirySaving = () => { clock.Set(session.StartsAt); holdWriteFailure.OnExpirySaving = null; };
        Assert.Equal(0, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        await AssertOriginalHoldStillAllocated(holds[0]);
        Assert.Equal(1, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        Assert.All(await db.SessionSeats.AsNoTracking().ToArrayAsync(), seat => Assert.Equal(SessionSeatStatus.Unavailable, seat.Status));
    }

    [PostgresFact]
    public async Task HoldExpiry_ConcurrentWorkersSkipLockedHoldAndCannotReleaseItsSuccessor()
    {
        var holds = await ExpiryHolds(1, 2);
        var hold = holds[0];
        clock.Set(hold.ExpiresAt);
        firstHoldClaim.Enabled = true;
        var pending = ExpiryProcessor(1).ProcessBatchAsync();
        Task<HttpResponseMessage>? cancellation = null;
        try
        {
            await firstHoldClaim.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var competing = await ExpiryProcessor(1).ProcessBatchAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, competing.Expired);
            await AssertOriginalHoldStillAllocated(hold);
            var token = (await LoginToken()).AccessToken;
            using var blocked = await AdminRequest(HttpMethod.Post, $"/api/sessions/{hold.SessionId}/holds", token,
                new CreateHoldRequest(hold.Items.Select(item => item.SessionSeatId).ToArray()));
            await AssertProblem(blocked, 409, "seat_not_available");
            cancellation = AdminRequest(HttpMethod.Delete, $"/api/holds/{hold.Id}", token);
            await firstHoldClaim.HoldTransitionStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(cancellation.IsCompleted);
        }
        finally { firstHoldClaim.Continue.TrySetResult(); }
        Assert.Equal(1, (await pending.WaitAsync(TimeSpan.FromSeconds(20))).Expired);
        using var cancelled = await cancellation!.WaitAsync(TimeSpan.FromSeconds(20));
        await AssertProblem(cancelled, 409, "hold_expired");
        var owner = (await LoginToken()).AccessToken;
        using var acquired = await AdminRequest(HttpMethod.Post, $"/api/sessions/{hold.SessionId}/holds", owner,
            new CreateHoldRequest(hold.Items.Select(item => item.SessionSeatId).ToArray()));
        Assert.Equal(HttpStatusCode.Created, acquired.StatusCode);
        Assert.Equal(0, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.All(await scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().SessionSeats.ToArrayAsync(), seat =>
        {
            Assert.Equal(SessionSeatStatus.Held, seat.Status);
            Assert.Equal(3, seat.Version);
        });
    }

    [PostgresFact]
    public async Task HoldExpiry_HostedServiceRunsImmediatelyAndStopsWithoutWaitingForPoll()
    {
        var holds = await ExpiryHolds(1);
        clock.Set(holds[0].ExpiresAt);
        var log = new ExpiryBatchLog();
        using var worker = new HoldExpiryWorker(ExpiryProcessor(), new HoldExpiryOptions { PollInterval = TimeSpan.FromHours(1) }, clock, log);
        await worker.StartAsync(CancellationToken.None);
        try { await log.Completed.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await worker.StopAsync(timeout.Token);
        }
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Equal(SeatHoldStatus.Expired, (await scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().SeatHolds.SingleAsync()).Status);
    }

    [PostgresTheory]
    [InlineData(SessionStatus.Cancelled)]
    [InlineData(SessionStatus.Completed)]
    public async Task HoldExpiry_NonScheduledSessionReleasesToUnavailable(SessionStatus status)
    {
        var holds = await ExpiryHolds(1, 2);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var session = await db.Sessions.SingleAsync();
        db.Entry(session).Property(value => value.Status).CurrentValue = status;
        await db.SaveChangesAsync();
        clock.Set(holds[0].ExpiresAt);
        Assert.Equal(1, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        Assert.All(await db.SessionSeats.AsNoTracking().ToArrayAsync(), seat =>
        {
            Assert.Equal(SessionSeatStatus.Unavailable, seat.Status);
            Assert.Equal(2, seat.Version);
        });
    }

    [PostgresFact]
    public async Task HoldExpiry_CancelledBatchLeavesHoldAndInventoryUnchanged()
    {
        var holds = await ExpiryHolds(1, 2);
        clock.Set(holds[0].ExpiresAt);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExpiryProcessor().ProcessBatchAsync(cancellationToken: stop.Token));
        await AssertOriginalHoldStillAllocated(holds[0]);
    }

    private HoldExpiryBatchProcessor ExpiryProcessor(int batchSize = 100) => new(
        app.Services.GetRequiredService<IServiceScopeFactory>(), clock, new HoldExpiryOptions { BatchSize = batchSize },
        NullLogger<HoldExpiryBatchProcessor>.Instance);

    private async Task<List<HoldResponse>> ExpiryHolds(int count, int seatsPerHold = 1)
    {
        var (token, session) = await HoldSession();
        var holds = new List<HoldResponse>();
        for (var index = 0; index < count; index++)
        {
            if (index > 0)
                session = await CreateAdmin<SessionResponse>("/api/admin/sessions", token,
                    new CreateSessionRequest(session.EventId, session.AuditoriumId, clock.GetUtcNow().AddHours(2), 100m, "UAH"));
            using var response = await AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", token,
                new CreateHoldRequest(session.Seats.Take(seatsPerHold).Select(seat => seat.Id).ToArray()));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            holds.Add((await response.Content.ReadFromJsonAsync<HoldResponse>())!);
        }
        return holds;
    }

    private sealed class ExpiryBatchLog : ILogger<HoldExpiryWorker>
    {
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Name == "HoldExpiryBatchCompleted") Completed.TrySetResult();
        }
    }
}
