using Microsoft.EntityFrameworkCore;
using Npgsql;
using TicketBooking.Domain;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Venue;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using VenueEntity = TicketBooking.Domain.Venue.Venue;

namespace TicketBooking.IntegrationTests;

public class SessionSeatConcurrencyTests : IAsyncLifetime
{
    private readonly string schema = "seat_concurrency_" + Guid.NewGuid().ToString("N");
    private readonly string adminConnection = Environment.GetEnvironmentVariable("TICKETBOOKING_TEST_CONNECTION_STRING")!;
    private string testConnection = null!;
    private Guid seatId;

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection);
        await command.ExecuteNonQueryAsync();
        testConnection = new NpgsqlConnectionStringBuilder(adminConnection) { SearchPath = schema }.ConnectionString;
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var venue = new VenueEntity(Guid.NewGuid(), "Cinema", "Test address");
        var auditorium = new Auditorium(Guid.NewGuid(), venue.Id, "Hall");
        var physicalSeat = new Seat(Guid.NewGuid(), auditorium.Id, "A", 1);
        var offering = new Event(Guid.NewGuid(), "Film", "", TimeSpan.FromMinutes(90), 0);
        var startsAt = new DateTimeOffset(2026, 11, 5, 20, 0, 0, TimeSpan.Zero);
        var session = new Session(Guid.NewGuid(), offering.Id, auditorium.Id, startsAt, startsAt.AddHours(2));
        seatId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        context.AddRange(venue, auditorium, physicalSeat, offering, session,
            new SessionSeat(seatId, session.Id, physicalSeat.Id, new Money(10m, "UAH")));
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (testConnection is null)
        {
            return;
        }

        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        // This generated schema belongs only to this test; no development tables are touched.
        await using var command = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task TwentyConcurrentHolds_AllowExactlyOneWriter()
    {
        var attempts = Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var context = CreateContext();
            return await Succeeds(() => new SessionSeatAllocationWriter(context).HoldAsync(seatId, 0));
        });

        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(result => result));
        Assert.Equal(19, results.Count(result => !result));
        await AssertStored(SessionSeatStatus.Held, 1);
    }

    [PostgresFact]
    public async Task HoldToBookingTransfer_AdvancesVersionEvenWhileStillHeld()
    {
        await using var context = CreateContext();
        var writer = new SessionSeatAllocationWriter(context);
        var holdVersion = await writer.HoldAsync(seatId, 0);
        var bookingVersion = await writer.TransferToBookingAsync(seatId, holdVersion);

        Assert.Equal(2, bookingVersion);
        await Assert.ThrowsAnyAsync<DbUpdateConcurrencyException>(() =>
            writer.ReleaseAsync(seatId, holdVersion, SessionSeatStatus.Held, SessionSeatStatus.Available));
        await AssertStored(SessionSeatStatus.Held, bookingVersion);
    }

    [PostgresFact]
    public async Task ConfirmationAndExpiryRelease_CannotBothWin()
    {
        await using (var context = CreateContext())
        {
            await new SessionSeatAllocationWriter(context).HoldAsync(seatId, 0);
        }

        await using var confirmationContext = CreateContext();
        await using var expiryContext = CreateContext();
        var results = await Task.WhenAll(
            Succeeds(() => new SessionSeatAllocationWriter(confirmationContext).ConfirmAsync(seatId, 1)),
            Succeeds(() => new SessionSeatAllocationWriter(expiryContext).ReleaseAsync(seatId, 1,
                SessionSeatStatus.Held, SessionSeatStatus.Available)));

        Assert.Single(results, result => result);
        await AssertStored(results[0] ? SessionSeatStatus.Booked : SessionSeatStatus.Available, 2);
    }

    [PostgresFact]
    public async Task StaleRelease_CannotReleaseASeatThatWasReleasedAndHeldAgain()
    {
        await using var context = CreateContext();
        var writer = new SessionSeatAllocationWriter(context);
        await writer.HoldAsync(seatId, 0);
        await writer.ReleaseAsync(seatId, 1, SessionSeatStatus.Held, SessionSeatStatus.Available);
        await writer.HoldAsync(seatId, 2);

        await Assert.ThrowsAnyAsync<DbUpdateConcurrencyException>(() =>
            writer.ReleaseAsync(seatId, 1, SessionSeatStatus.Held, SessionSeatStatus.Available));
        await AssertStored(SessionSeatStatus.Held, 3);
    }

    [PostgresFact]
    public async Task TrackedSyncAndAsyncWrites_CheckOriginalVersionAndRejectRepeatedStaleSaves()
    {
        await using var first = CreateContext();
        await using var stale = CreateContext();
        var firstSeat = await first.SessionSeats.SingleAsync();
        var staleSeat = await stale.SessionSeats.SingleAsync();
        first.Entry(firstSeat).Property(value => value.Status).CurrentValue = SessionSeatStatus.Held;
        first.SaveChanges();
        Assert.Equal(1, firstSeat.Version);
        stale.Entry(staleSeat).Property(value => value.Status).CurrentValue = SessionSeatStatus.Held;

        await Assert.ThrowsAnyAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        Assert.Equal(1, staleSeat.Version);
        Assert.Throws<DbUpdateConcurrencyException>(() => stale.SaveChanges());
        Assert.Equal(1, staleSeat.Version);
        Assert.Equal(0, stale.Entry(staleSeat).Property(value => value.Version).OriginalValue);
        await AssertStored(SessionSeatStatus.Held, 1);
    }

    [PostgresFact]
    public async Task ExplicitTrackedWrite_AdvancesTokenForSameStatusOwnershipTransfer()
    {
        await using (var context = CreateContext())
        {
            await new SessionSeatAllocationWriter(context).HoldAsync(seatId, 0);
        }

        await using var transfer = CreateContext();
        var seat = await transfer.SessionSeats.SingleAsync();
        transfer.Entry(seat).State = EntityState.Modified;
        await transfer.SaveChangesAsync();

        await AssertStored(SessionSeatStatus.Held, 2);
    }

    [PostgresFact]
    public async Task PriceChanges_AlsoAdvanceTokenAndInvalidateStaleAllocationWrites()
    {
        await using var context = CreateContext();
        var seat = await context.SessionSeats.SingleAsync();
        context.Entry(seat).ComplexProperty(value => value.Price).Property(value => value.Amount).CurrentValue = 20m;
        await context.SaveChangesAsync();
        Assert.Equal(1, seat.Version);

        await Assert.ThrowsAnyAsync<DbUpdateConcurrencyException>(() =>
            new SessionSeatAllocationWriter(context).HoldAsync(seatId, 0));
        await AssertStored(SessionSeatStatus.Available, 1);
    }

    [PostgresFact]
    public async Task TrackedDelete_CannotDeleteASeatAfterAnotherAllocationWrite()
    {
        await using var stale = CreateContext();
        var seat = await stale.SessionSeats.SingleAsync();
        await using (var winner = CreateContext())
        {
            await new SessionSeatAllocationWriter(winner).HoldAsync(seatId, 0);
        }

        stale.Remove(seat);
        await Assert.ThrowsAnyAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await AssertStored(SessionSeatStatus.Held, 1);
    }

    [PostgresFact]
    public async Task BatchConflict_RollsBackEarlierSeatChanges()
    {
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        await using (var seed = CreateContext())
        {
            var first = await seed.SessionSeats.SingleAsync();
            var session = await seed.Sessions.SingleAsync();
            var physicalSeat = new Seat(Guid.NewGuid(), session.AuditoriumId, "A", 2);
            seed.AddRange(physicalSeat, new SessionSeat(secondId, first.SessionId, physicalSeat.Id, new Money(10m, "UAH")));
            await seed.SaveChangesAsync();
        }

        await using var batch = CreateContext();
        var seats = await batch.SessionSeats.OrderBy(seat => seat.Id).ToArrayAsync();
        await using (var winner = CreateContext())
        {
            await new SessionSeatAllocationWriter(winner).HoldAsync(secondId, 0);
        }

        foreach (var seat in seats)
        {
            batch.Entry(seat).Property(value => value.Status).CurrentValue = SessionSeatStatus.Held;
        }

        await Assert.ThrowsAnyAsync<DbUpdateConcurrencyException>(() => batch.SaveChangesAsync());
        await AssertStored(SessionSeatStatus.Available, 0);
        await using var verify = CreateContext();
        var second = await verify.SessionSeats.SingleAsync(seat => seat.Id == secondId);
        Assert.Equal(SessionSeatStatus.Held, second.Status);
        Assert.Equal(1, second.Version);
    }

    [PostgresFact]
    public async Task SetBasedWrites_DoNotSilentlyRefreshTrackedSeats()
    {
        await using var context = CreateContext();
        var stale = await context.SessionSeats.SingleAsync();
        await new SessionSeatAllocationWriter(context).HoldAsync(seatId, 0);
        Assert.Equal(0, stale.Version);
        context.Entry(stale).Property(value => value.Status).CurrentValue = SessionSeatStatus.Unavailable;

        await Assert.ThrowsAnyAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());
        await AssertStored(SessionSeatStatus.Held, 1);
    }

    [PostgresFact]
    public async Task CompleteAllocationSequence_AdvancesEveryOwnershipWriteAndCanReleaseAsUnavailable()
    {
        await using var context = CreateContext();
        var writer = new SessionSeatAllocationWriter(context);

        Assert.Equal(1, await writer.HoldAsync(seatId, 0));
        Assert.Equal(2, await writer.TransferToBookingAsync(seatId, 1));
        Assert.Equal(3, await writer.ConfirmAsync(seatId, 2));
        Assert.Equal(4, await writer.ReleaseAsync(seatId, 3, SessionSeatStatus.Booked, SessionSeatStatus.Unavailable));
        await Assert.ThrowsAnyAsync<DbUpdateConcurrencyException>(() => writer.ConfirmAsync(seatId, 2));
        await AssertStored(SessionSeatStatus.Unavailable, 4);
    }

    [PostgresFact]
    public async Task Database_RejectsNegativeStoredVersions()
    {
        await using var context = CreateContext();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"SessionSeats\" SET \"Version\" = {-1L} WHERE \"Id\" = {seatId}"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("CK_SessionSeats_Version_NonNegative", exception.ConstraintName);
        await AssertStored(SessionSeatStatus.Available, 0);
    }

    private TicketBookingDbContext CreateContext() => PersistenceModelTests.CreateContext(testConnection);

    private async Task AssertStored(SessionSeatStatus status, long version)
    {
        await using var context = CreateContext();
        var seat = await context.SessionSeats.SingleAsync(value => value.Id == seatId);
        Assert.Equal(status, seat.Status);
        Assert.Equal(version, seat.Version);
    }

    private static async Task<bool> Succeeds(Func<Task<long>> write)
    {
        try
        {
            await write();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}
