using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TicketBooking.Domain.Booking;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresFact]
    public async Task HoldCleanup_ConversionWinsAndOriginalCleanupCannotReleaseBookingSeats()
    {
        var setup = await OwnedHold();
        firstHoldClaim.Enabled = true;
        var converting = ConvertHold(setup.Hold.Id, setup.Owner);
        Task<HttpResponseMessage>? cancelling = null;
        try
        {
            await firstHoldClaim.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            cancelling = AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner);
            await firstHoldClaim.SecondHoldTransitionStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(cancelling.IsCompleted);
        }
        finally { firstHoldClaim.Continue.TrySetResult(); }
        using var converted = await converting.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(HttpStatusCode.Created, converted.StatusCode);
        using var cancelled = await cancelling!.WaitAsync(TimeSpan.FromSeconds(20));
        await AssertProblem(cancelled, 409, "hold_not_active");
        clock.Set(setup.Hold.ExpiresAt.AddMinutes(1));
        Assert.Equal(0, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.False(await scope.ServiceProvider.GetRequiredService<HoldExpiryReleaser>().ExpireAsync(setup.Hold.Id));
        var hold = await db.SeatHolds.Include(value => value.Items).SingleAsync();
        var booking = await db.Bookings.Include(value => value.Items).SingleAsync();
        Assert.Equal(SeatHoldStatus.Converted, hold.Status);
        Assert.Equal(BookingStatus.PendingPayment, booking.Status);
        Assert.True(booking.PaymentDeadline > clock.GetUtcNow());
        Assert.Equal(setup.Hold.ExpiresAt, hold.ExpiresAt);
        Assert.Equal(setup.OwnerId, booking.UserId);
        Assert.Equal(hold.TotalAmount, booking.TotalAmount);
        Assert.Equal(2, booking.Items.Count);
        foreach (var item in booking.Items)
        {
            var source = hold.Items.Single(value => value.SessionSeatId == item.SessionSeatId);
            Assert.Equal(1, source.AllocationVersion);
            Assert.Equal(2, item.AllocationVersion);
            Assert.Equal(source.PriceSnapshot, item.PriceSnapshot);
            await Assert.ThrowsAsync<SeatAllocationConflictException>(() => new SessionSeatAllocationWriter(db)
                .ReleaseAsync(item.SessionSeatId, source.AllocationVersion!.Value, SessionSeatStatus.Held, SessionSeatStatus.Available));
        }
        Assert.All(await db.SessionSeats.AsNoTracking().ToArrayAsync(), seat =>
        {
            Assert.Equal(SessionSeatStatus.Held, seat.Status);
            Assert.Equal(2, seat.Version);
        });
        using var repeat = await ConvertHold(setup.Hold.Id, setup.Owner);
        await AssertProblem(repeat, 409, "hold_not_active");
        Assert.Equal(1, await db.Bookings.CountAsync());
    }

    [PostgresFact]
    public async Task HoldCleanup_CancellationWinsAndReplayAfterExpiryDoesNotReleaseSuccessor()
    {
        var setup = await OwnedHold();
        firstHoldClaim.Enabled = true;
        var cancelling = AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        Task<HttpResponseMessage>? converting = null;
        try
        {
            await firstHoldClaim.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            converting = ConvertHold(setup.Hold.Id, setup.Owner);
            await firstHoldClaim.SecondHoldTransitionStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(converting.IsCompleted);
        }
        finally { firstHoldClaim.Continue.TrySetResult(); }
        using var cancelled = await cancelling.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        using var conversion = await converting!.WaitAsync(TimeSpan.FromSeconds(20));
        await AssertProblem(conversion, 409, "hold_not_active");
        clock.Set(setup.Hold.ExpiresAt);
        using var acquired = await AdminRequest(HttpMethod.Post, $"/api/sessions/{setup.Session.Id}/holds", setup.Owner,
            new TicketBooking.Contracts.Reservation.CreateHoldRequest(setup.Hold.Items.Select(item => item.SessionSeatId).ToArray()));
        Assert.Equal(HttpStatusCode.Created, acquired.StatusCode);
        using var repeated = await AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        using var otherOwner = await AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Admin);
        await AssertProblem(otherOwner, 403, "resource_not_owned");
        Assert.Equal(0, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Empty(await db.Bookings.ToArrayAsync());
        Assert.Equal(SeatHoldStatus.Cancelled, (await db.SeatHolds.SingleAsync(value => value.Id == setup.Hold.Id)).Status);
        Assert.All(await db.SessionSeats.ToArrayAsync(), seat =>
        {
            Assert.Equal(SessionSeatStatus.Held, seat.Status);
            Assert.Equal(3, seat.Version);
        });
    }

    [PostgresFact]
    public async Task HoldCleanup_ExpiryWinsAndConversionCannotReviveOrReleaseItAgain()
    {
        var setup = await OwnedHold();
        clock.Set(setup.Hold.ExpiresAt);
        firstHoldClaim.Enabled = true;
        var expiring = ExpiryProcessor().ProcessBatchAsync();
        Task<HttpResponseMessage>? converting = null;
        try
        {
            await firstHoldClaim.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            converting = ConvertHold(setup.Hold.Id, setup.Owner);
            await firstHoldClaim.HoldTransitionStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(converting.IsCompleted);
        }
        finally { firstHoldClaim.Continue.TrySetResult(); }
        Assert.Equal(1, (await expiring.WaitAsync(TimeSpan.FromSeconds(20))).Expired);
        using var converted = await converting!.WaitAsync(TimeSpan.FromSeconds(20));
        await AssertProblem(converted, 409, "hold_expired");
        await AssertExpiredExactlyOnce(setup.Hold.Id);
    }

    [PostgresFact]
    public async Task HoldCleanup_ConversionCrossingDeadlineRollsBackBeforeExpiryCanProceed()
    {
        var setup = await OwnedHold();
        firstHoldClaim.Enabled = true;
        var converting = ConvertHold(setup.Hold.Id, setup.Owner);
        try
        {
            await firstHoldClaim.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            clock.Set(setup.Hold.ExpiresAt);
            Assert.Equal(0, (await ExpiryProcessor().ProcessBatchAsync().WaitAsync(TimeSpan.FromSeconds(10))).Expired);
        }
        finally { firstHoldClaim.Continue.TrySetResult(); }
        using var conversion = await converting.WaitAsync(TimeSpan.FromSeconds(20));
        await AssertProblem(conversion, 409, "hold_expired");
        await AssertOriginalHoldStillAllocated(setup.Hold);
        Assert.Equal(1, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        await AssertExpiredExactlyOnce(setup.Hold.Id);
    }

    [PostgresFact]
    public async Task HoldCleanup_ConcurrentConversionsCreateOnlyOneBooking()
    {
        var setup = await OwnedHold();
        firstHoldClaim.Enabled = true;
        var first = ConvertHold(setup.Hold.Id, setup.Owner);
        Task<HttpResponseMessage>? second = null;
        try
        {
            await firstHoldClaim.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            second = ConvertHold(setup.Hold.Id, setup.Owner);
            await firstHoldClaim.SecondHoldTransitionStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(second.IsCompleted);
        }
        finally { firstHoldClaim.Continue.TrySetResult(); }
        var responses = await Task.WhenAll(first, second!).WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            await AssertProblem(Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict), 409, "hold_not_active");
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            Assert.Equal(1, await db.Bookings.CountAsync());
            Assert.Equal(setup.Hold.Id, (await db.Bookings.SingleAsync()).SourceHoldId);
            Assert.Equal(SeatHoldStatus.Converted, (await db.SeatHolds.SingleAsync()).Status);
            Assert.Equal(2, await db.BookingItems.CountAsync());
            Assert.All(await db.SessionSeats.ToArrayAsync(), seat => Assert.Equal(2, seat.Version));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [PostgresTheory]
    [InlineData("legacy")]
    [InlineData("transferred")]
    [InlineData("booked")]
    public async Task HoldCleanup_UnknownOrStaleOwnershipPreventsConversionAndEveryRelease(string scenario)
    {
        var setup = await OwnedHold();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var last = await db.SessionSeats.OrderByDescending(seat => seat.Id).FirstAsync();
        if (scenario == "legacy")
        {
            var item = await db.SeatHoldItems.SingleAsync(value => value.SessionSeatId == last.Id);
            db.Entry(item).Property(value => value.AllocationVersion).CurrentValue = null;
            await db.SaveChangesAsync();
        }
        else if (scenario == "transferred")
            Assert.Equal(2, await new SessionSeatAllocationWriter(db).TransferToBookingAsync(last.Id, 1));
        else Assert.Equal(2, await new SessionSeatAllocationWriter(db).ConfirmAsync(last.Id, 1));
        var before = await db.SessionSeats.AsNoTracking().ToDictionaryAsync(seat => seat.Id, seat => new { seat.Status, seat.Version });
        using var conversion = await ConvertHold(setup.Hold.Id, setup.Owner);
        await AssertProblem(conversion, 409, "seat_not_available");
        using var cancellation = await AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        await AssertProblem(cancellation, 409, "seat_not_available");
        clock.Set(setup.Hold.ExpiresAt);
        Assert.Equal(0, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        db.ChangeTracker.Clear();
        Assert.Equal(SeatHoldStatus.Active, (await db.SeatHolds.SingleAsync()).Status);
        Assert.Empty(await db.Bookings.ToArrayAsync());
        Assert.Empty(await db.BookingItems.ToArrayAsync());
        Assert.All(await db.SessionSeats.ToArrayAsync(), seat =>
        {
            Assert.Equal(before[seat.Id].Status, seat.Status);
            Assert.Equal(before[seat.Id].Version, seat.Version);
        });
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HoldCleanup_ConversionSaveFailureOrLateCommitRollsBackTransfersAndBooking(bool expireDuringSave)
    {
        var setup = await OwnedHold();
        holdWriteFailure.FailConversion = !expireDuringSave;
        if (expireDuringSave) holdWriteFailure.OnConversionSaving = () => clock.Set(setup.Hold.ExpiresAt);
        using var conversion = await ConvertHold(setup.Hold.Id, setup.Owner);
        await AssertProblem(conversion, expireDuringSave ? 409 : 500, expireDuringSave ? "hold_expired" : "internal_error");
        await AssertOriginalHoldStillAllocated(setup.Hold);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Empty(await db.Bookings.ToArrayAsync());
        Assert.Empty(await db.BookingItems.ToArrayAsync());
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HoldCleanup_ConversionRequiresTheStoredOwnerAndAnExistingHold(bool missing)
    {
        var setup = await OwnedHold();
        using var response = await ConvertHold(missing ? Guid.NewGuid() : setup.Hold.Id, setup.Admin);
        await AssertProblem(response, missing ? 404 : 403, missing ? "not_found" : "resource_not_owned");
        await AssertOriginalHoldStillAllocated(setup.Hold);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Empty(await db.Bookings.ToArrayAsync());
        Assert.Empty(await db.BookingItems.ToArrayAsync());
    }

    [PostgresTheory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task HoldCleanup_BookingClaimTokenConstraintRejectsNonpositiveVersions(long version)
    {
        var setup = await OwnedHold();
        using var conversion = await ConvertHold(setup.Hold.Id, setup.Owner);
        Assert.Equal(HttpStatusCode.Created, conversion.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"BookingItems\" SET \"AllocationVersion\" = {version}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("CK_BookingItems_AllocationVersion_Positive", error.ConstraintName);
    }

    [PostgresTheory]
    [InlineData("cancelled")]
    [InlineData("expired")]
    [InlineData("converted")]
    public async Task HoldCleanup_StaleTrackedConversionCannotOverwriteWinnerOrInsertAnotherBooking(string winner)
    {
        var setup = await OwnedHold();
        await using var staleScope = app.Services.CreateAsyncScope();
        var staleDb = staleScope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var staleHold = await staleDb.SeatHolds.Include(value => value.Items).SingleAsync();
        var staleBooking = TicketBooking.Domain.Booking.Booking.CreateFromHold(Guid.NewGuid(), staleHold, setup.OwnerId,
            "STALE-" + Guid.NewGuid().ToString("N"), clock, setup.Session.StartsAt,
            staleScope.ServiceProvider.GetRequiredService<TicketBooking.Domain.BookingTimingPolicy>());
        staleDb.Bookings.Add(staleBooking);
        staleHold.Convert(setup.OwnerId, clock);
        if (winner == "cancelled")
        {
            using var response = await AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        else if (winner == "expired")
        {
            clock.Set(setup.Hold.ExpiresAt);
            Assert.Equal(1, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        }
        else
        {
            using var response = await ConvertHold(setup.Hold.Id, setup.Owner);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        var failure = await Assert.ThrowsAnyAsync<DbUpdateException>(() => staleDb.SaveChangesAsync());
        if (failure is not DbUpdateConcurrencyException)
        {
            var postgres = Assert.IsType<PostgresException>(failure.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
            Assert.Equal("IX_Bookings_SourceHoldId", postgres.ConstraintName);
        }
        staleDb.ChangeTracker.Clear();
        var expected = winner == "cancelled" ? SeatHoldStatus.Cancelled :
            winner == "expired" ? SeatHoldStatus.Expired : SeatHoldStatus.Converted;
        Assert.Equal(expected, (await staleDb.SeatHolds.SingleAsync()).Status);
        Assert.Equal(winner == "converted" ? 1 : 0, await staleDb.Bookings.CountAsync());
        Assert.DoesNotContain(await staleDb.Bookings.Select(value => value.Id).ToArrayAsync(), id => id == staleBooking.Id);
        Assert.All(await staleDb.SessionSeats.ToArrayAsync(), seat =>
        {
            Assert.Equal(winner == "converted" ? SessionSeatStatus.Held : SessionSeatStatus.Available, seat.Status);
            Assert.Equal(2, seat.Version);
        });
    }

    private Task<HttpResponseMessage> ConvertHold(Guid holdId, string token) =>
        AdminRequest(HttpMethod.Post, "/api/bookings", token, new TicketBooking.Contracts.Booking.CreateBookingRequest(holdId));

    private async Task AssertExpiredExactlyOnce(Guid holdId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.False(await scope.ServiceProvider.GetRequiredService<HoldExpiryReleaser>().ExpireAsync(holdId));
        Assert.Equal(0, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        Assert.Empty(await db.Bookings.ToArrayAsync());
        Assert.Equal(SeatHoldStatus.Expired, (await db.SeatHolds.SingleAsync()).Status);
        Assert.All(await db.SessionSeats.ToArrayAsync(), seat =>
        {
            Assert.Equal(SessionSeatStatus.Available, seat.Status);
            Assert.Equal(2, seat.Version);
        });
    }
}
