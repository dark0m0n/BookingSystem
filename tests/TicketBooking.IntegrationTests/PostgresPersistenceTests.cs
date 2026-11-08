using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using TicketBooking.Domain;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;
using TicketBooking.Domain.Venue;
using TicketBooking.Infrastructure.Persistence;
using BookingEntity = TicketBooking.Domain.Booking.Booking;
using BookingItem = TicketBooking.Domain.Booking.BookingItem;
using PaymentEntity = TicketBooking.Domain.Payment.Payment;
using VenueEntity = TicketBooking.Domain.Venue.Venue;

namespace TicketBooking.IntegrationTests;

public class PostgresPersistenceTests : IAsyncLifetime
{
    private const string ConnectionVariable = "TICKETBOOKING_TEST_CONNECTION_STRING";
    private readonly TicketBookingDbContext context = PersistenceModelTests.CreateContext(
        Environment.GetEnvironmentVariable(ConnectionVariable));
    private IDbContextTransaction? transaction;
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);
    private static readonly BookingTimingPolicy Timing = new(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1));

    public async Task InitializeAsync()
    {
        await context.Database.MigrateAsync();
        transaction = await context.Database.BeginTransactionAsync();
    }

    public async Task DisposeAsync()
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync();
            await transaction.DisposeAsync();
        }

        await context.DisposeAsync();
    }

    [PostgresTheory]
    [InlineData(SessionSeatStatus.Available)]
    [InlineData(SessionSeatStatus.Held)]
    [InlineData(SessionSeatStatus.Booked)]
    [InlineData(SessionSeatStatus.Unavailable)]
    public async Task VenueAndCatalogueRoundTrip_PreservesEveryFieldAndInventoryStatus(SessionSeatStatus status)
    {
        var venue = new VenueEntity(Guid.NewGuid(), "Кінотеатр", "Kyiv, Test Street 12");
        var auditorium = new Auditorium(Guid.NewGuid(), venue.Id, "Screen 2");
        var seat = new Seat(Guid.NewGuid(), auditorium.Id, "Б", 7, SeatType.Premium);
        var offering = new Event(Guid.NewGuid(), "Test film", "A persisted description", TimeSpan.FromMinutes(105),
            16, EventStatus.Published);
        var startsAt = Now.AddHours(2).ToOffset(TimeSpan.FromHours(2));
        var session = new Session(Guid.NewGuid(), offering.Id, auditorium.Id, startsAt, startsAt.Add(offering.Duration),
            SessionStatus.Cancelled);
        var inventory = new SessionSeat(Guid.NewGuid(), session.Id, seat.Id, new Money(25.75m, "EUR"), status);
        context.AddRange(venue, auditorium, seat, offering, session, inventory);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var storedVenue = await context.Venues.SingleAsync(value => value.Id == venue.Id);
        var storedAuditorium = await context.Auditoriums.SingleAsync(value => value.Id == auditorium.Id);
        var storedSeat = await context.Seats.SingleAsync(value => value.Id == seat.Id);
        var storedEvent = await context.Events.SingleAsync(value => value.Id == offering.Id);
        var storedSession = await context.Sessions.SingleAsync(value => value.Id == session.Id);
        var storedInventory = await context.SessionSeats.SingleAsync(value => value.Id == inventory.Id);

        Assert.Equal((venue.Name, venue.Address), (storedVenue.Name, storedVenue.Address));
        Assert.Equal((venue.Id, auditorium.Name), (storedAuditorium.VenueId, storedAuditorium.Name));
        Assert.Equal((auditorium.Id, seat.Row, seat.Number, seat.Type),
            (storedSeat.AuditoriumId, storedSeat.Row, storedSeat.Number, storedSeat.Type));
        Assert.Equal((offering.Title, offering.Description, offering.Duration, offering.AgeRating, offering.Status),
            (storedEvent.Title, storedEvent.Description, storedEvent.Duration, storedEvent.AgeRating, storedEvent.Status));
        Assert.Equal((offering.Id, auditorium.Id, session.StartsAt, session.EndsAt, session.Status),
            (storedSession.EventId, storedSession.AuditoriumId, storedSession.StartsAt, storedSession.EndsAt, storedSession.Status));
        Assert.Equal(TimeSpan.Zero, storedSession.StartsAt.Offset);
        Assert.Equal(TimeSpan.Zero, storedSession.EndsAt.Offset);
        Assert.Equal((session.Id, seat.Id, inventory.Price, status, inventory.Version),
            (storedInventory.SessionId, storedInventory.SeatId, storedInventory.Price, storedInventory.Status, storedInventory.Version));
    }

    [PostgresTheory]
    [InlineData(SeatHoldStatus.Active)]
    [InlineData(SeatHoldStatus.Converted)]
    [InlineData(SeatHoldStatus.Cancelled)]
    [InlineData(SeatHoldStatus.Expired)]
    public async Task SeatHoldRoundTrip_PreservesOwnershipItemsTotalDeadlineAndStatus(SeatHoldStatus status)
    {
        var inventory = await SeedInventory();
        var secondSeat = new Seat(Guid.NewGuid(), inventory.Auditorium.Id, "A", 2);
        var secondInventory = new SessionSeat(Guid.NewGuid(), inventory.Session.Id, secondSeat.Id, new Money(7.25m, "UAH"));
        context.AddRange(secondSeat, secondInventory);
        var hold = new SeatHold(Guid.NewGuid(), inventory.Session.Id, Guid.NewGuid(), inventory.Session.StartsAt,
            [inventory.Inventory, secondInventory], TimeProviderAtNow(), Timing);
        switch (status)
        {
            case SeatHoldStatus.Converted:
                hold.Convert(hold.UserId, TimeProviderAtNow());
                break;
            case SeatHoldStatus.Cancelled:
                hold.Cancel(hold.UserId, TimeProviderAtNow());
                break;
            case SeatHoldStatus.Expired:
                hold.Expire(new FixedTimeProvider(hold.ExpiresAt));
                break;
        }

        context.SeatHolds.Add(hold);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var stored = await context.SeatHolds.Include(value => value.Items).SingleAsync(value => value.Id == hold.Id);

        Assert.Equal(hold.SessionId, stored.SessionId);
        Assert.Equal(hold.UserId, stored.UserId);
        Assert.Equal(status, stored.Status);
        Assert.Equal(hold.ExpiresAt, stored.ExpiresAt);
        Assert.Equal(TimeSpan.Zero, stored.ExpiresAt.Offset);
        Assert.Equal(new Money(19.75m, "UAH"), stored.TotalAmount);
        Assert.Equal(2, stored.Items.Count);
        Assert.All(stored.Items, item => Assert.Equal(hold.Id, item.SeatHoldId));
        Assert.Equal(inventory.Inventory.Price, stored.Items.Single(item => item.SessionSeatId == inventory.Inventory.Id).PriceSnapshot);
        Assert.Equal(secondInventory.Price, stored.Items.Single(item => item.SessionSeatId == secondInventory.Id).PriceSnapshot);
        Assert.Throws<NotSupportedException>(() => ((IList<SeatHoldItem>)stored.Items).Clear());
    }

    [PostgresFact]
    public async Task MigrationAndRoundTrip_PreserveSnapshotsDeadlinesAndReadOnlyCollections()
    {
        var inventory = await SeedInventory();
        var hold = CreateHold(inventory);
        var booking = CreateBooking(hold);
        hold.Convert(hold.UserId, TimeProviderAtNow());
        var payment = PaymentEntity.CreateForBooking(Guid.NewGuid(), booking, "request-1", TimeProviderAtNow());
        payment.Succeed("provider-1");
        booking.Confirm(TimeProviderAtNow());
        context.AddRange(hold, booking, payment);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var storedHold = await context.SeatHolds.Include(value => value.Items).SingleAsync(value => value.Id == hold.Id);
        var storedBooking = await context.Bookings.Include(value => value.Items).SingleAsync(value => value.Id == booking.Id);
        var storedPayment = await context.Payments.SingleAsync(value => value.Id == payment.Id);
        var storedSeat = await context.SessionSeats.SingleAsync(value => value.Id == inventory.Inventory.Id);

        Assert.Equal(hold.ExpiresAt, storedHold.ExpiresAt);
        Assert.Equal(SeatHoldStatus.Converted, storedHold.Status);
        Assert.Equal(booking.PaymentDeadline, storedBooking.PaymentDeadline);
        Assert.Equal(booking.CancellationDeadline, storedBooking.CancellationDeadline);
        Assert.Equal(booking.SessionStartsAt, storedBooking.SessionStartsAt);
        Assert.Equal(booking.TotalAmount, storedBooking.TotalAmount);
        Assert.Equal(booking.TotalAmount, storedPayment.Amount);
        Assert.Equal(storedSeat.Price, Assert.Single(storedHold.Items).PriceSnapshot);
        Assert.Equal(storedSeat.Price, Assert.Single(storedBooking.Items).PriceSnapshot);
        Assert.Equal("provider-1", storedPayment.ProviderReference);
        Assert.Throws<NotSupportedException>(() => ((IList<SeatHoldItem>)storedHold.Items).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<BookingItem>)storedBooking.Items).Clear());
    }

    [PostgresTheory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CatalogueRepricing_DoesNotChangeHoldBookingOrPaymentSnapshots(int seatCount)
    {
        var inventory = await SeedInventory();
        var selected = new List<SessionSeat> { inventory.Inventory };
        if (seatCount == 2)
        {
            var physicalSeat = new Seat(Guid.NewGuid(), inventory.Auditorium.Id, "A", 2);
            // Even catalogue rows initialized from the same Money value must be independent.
            var another = new SessionSeat(Guid.NewGuid(), inventory.Session.Id, physicalSeat.Id, inventory.Inventory.Price);
            context.AddRange(physicalSeat, another);
            await context.SaveChangesAsync();
            selected.Add(another);
        }

        var hold = new SeatHold(Guid.NewGuid(), inventory.Session.Id, Guid.NewGuid(), inventory.Session.StartsAt,
            selected, TimeProviderAtNow(), Timing);
        context.SeatHolds.Add(hold);
        await context.SaveChangesAsync();

        // Reprice the actual tracked complex value, not a replacement object in a detached list.
        var currentPrice = context.Entry(inventory.Inventory).ComplexProperty(seat => seat.Price);
        currentPrice.Property(price => price.Amount).CurrentValue = 25m;
        currentPrice.Property(price => price.Currency).CurrentValue = "EUR";
        Assert.All(hold.Items, item => Assert.Equal(new Money(12.50m, "UAH"), item.PriceSnapshot));
        Assert.Equal(new Money(12.50m * seatCount, "UAH"), hold.TotalAmount);
        Assert.Equal(EntityState.Unchanged, context.Entry(hold).State);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Conversion copies the persisted hold prices, despite a new catalogue amount/currency.
        var sourceHold = await context.SeatHolds.Include(value => value.Items).SingleAsync(value => value.Id == hold.Id);
        var booking = CreateBooking(sourceHold);
        var payment = PaymentEntity.CreateForBooking(Guid.NewGuid(), booking, "snapshot-payment", TimeProviderAtNow());
        context.AddRange(booking, payment);
        await context.SaveChangesAsync();

        var currentSeat = await context.SessionSeats.SingleAsync(value => value.Id == inventory.Inventory.Id);
        context.Entry(currentSeat).ComplexProperty(seat => seat.Price).Property(price => price.Amount).CurrentValue = 50m;
        context.Entry(currentSeat).ComplexProperty(seat => seat.Price).Property(price => price.Currency).CurrentValue = "USD";
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var storedHold = await context.SeatHolds.Include(value => value.Items).SingleAsync(value => value.Id == hold.Id);
        var storedBooking = await context.Bookings.Include(value => value.Items).SingleAsync(value => value.Id == booking.Id);
        var storedPayment = await context.Payments.SingleAsync(value => value.Id == payment.Id);
        var repricedSeat = await context.SessionSeats.SingleAsync(value => value.Id == inventory.Inventory.Id);
        var originalTotal = new Money(12.50m * seatCount, "UAH");

        Assert.Equal(new Money(50m, "USD"), repricedSeat.Price);
        Assert.Equal(2, repricedSeat.Version);
        Assert.All(storedHold.Items, item => Assert.Equal(new Money(12.50m, "UAH"), item.PriceSnapshot));
        Assert.All(storedBooking.Items, item => Assert.Equal(new Money(12.50m, "UAH"), item.PriceSnapshot));
        Assert.Equal(originalTotal, storedHold.TotalAmount);
        Assert.Equal(originalTotal, storedBooking.TotalAmount);
        Assert.Equal(originalTotal, storedPayment.Amount);
        if (seatCount == 2)
        {
            Assert.Equal(new Money(12.50m, "UAH"),
                (await context.SessionSeats.SingleAsync(value => value.Id == selected[1].Id)).Price);
        }
    }

    [PostgresFact]
    public async Task DuplicateSeatCoordinates_AreRejectedWithinAnAuditorium()
    {
        var inventory = await SeedInventory();
        context.Seats.Add(new Seat(Guid.NewGuid(), inventory.Auditorium.Id, "A", 1));

        await AssertConstraint(PostgresErrorCodes.UniqueViolation, "IX_Seats_AuditoriumId_Row_Number");
    }

    [PostgresFact]
    public async Task SeatCoordinates_CanRepeatInAnotherAuditorium()
    {
        var inventory = await SeedInventory();
        var auditorium = new Auditorium(Guid.NewGuid(), inventory.Venue.Id, "Second hall");
        context.AddRange(auditorium, new Seat(Guid.NewGuid(), auditorium.Id, "A", 1));

        await context.SaveChangesAsync();
        Assert.Equal(2, await context.Seats.CountAsync());
    }

    [PostgresFact]
    public async Task DuplicateSessionSeatPair_IsRejected()
    {
        var inventory = await SeedInventory();
        context.SessionSeats.Add(new SessionSeat(Guid.NewGuid(), inventory.Session.Id, inventory.Seat.Id, new Money(20m, "UAH")));

        await AssertConstraint(PostgresErrorCodes.UniqueViolation, "IX_SessionSeats_SessionId_SeatId");
    }

    [PostgresFact]
    public async Task PhysicalSeat_CanBeReusedInAnotherSession()
    {
        var inventory = await SeedInventory();
        var session = new Session(Guid.NewGuid(), inventory.Event.Id, inventory.Auditorium.Id, Now.AddHours(4), Now.AddHours(6));
        context.AddRange(session, new SessionSeat(Guid.NewGuid(), session.Id, inventory.Seat.Id, new Money(20m, "UAH")));

        await context.SaveChangesAsync();
        Assert.Equal(2, await context.SessionSeats.CountAsync());
    }

    [PostgresTheory]
    [InlineData("Seat")]
    [InlineData("Session")]
    public async Task MissingSessionSeatParent_IsRejected(string missingParent)
    {
        var inventory = await SeedInventory();
        context.SessionSeats.Add(new SessionSeat(Guid.NewGuid(),
            missingParent == "Session" ? Guid.NewGuid() : inventory.Session.Id,
            missingParent == "Seat" ? Guid.NewGuid() : inventory.Seat.Id, new Money(1m, "UAH")));

        await AssertConstraint(PostgresErrorCodes.ForeignKeyViolation,
            missingParent == "Session" ? "FK_SessionSeats_Sessions_SessionId" : "FK_SessionSeats_Seats_SeatId");
    }

    [PostgresFact]
    public async Task DeletingAnAuditoriumWithSeats_IsRestricted()
    {
        var inventory = await SeedInventory();
        // Raw deletion lets PostgreSQL, rather than EF relationship fixup, enforce the foreign key.
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"Auditoriums\" WHERE \"Id\" = {inventory.Auditorium.Id}"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [PostgresFact]
    public async Task BookingNumber_IsUnique()
    {
        var inventory = await SeedInventory();
        var hold = CreateHold(inventory);
        context.Bookings.Add(CreateBooking(hold));
        await context.SaveChangesAsync();
        context.Bookings.Add(CreateBooking(hold));

        await AssertConstraint(PostgresErrorCodes.UniqueViolation, "IX_Bookings_Number");
    }

    [PostgresFact]
    public async Task PaymentKey_IsUniqueWithinABooking()
    {
        var inventory = await SeedInventory();
        var booking = CreateBooking(CreateHold(inventory));
        context.AddRange(booking, PaymentEntity.CreateForBooking(Guid.NewGuid(), booking, "same-key", TimeProviderAtNow()));
        await context.SaveChangesAsync();
        context.Payments.Add(PaymentEntity.CreateForBooking(Guid.NewGuid(), booking, "same-key", TimeProviderAtNow()));

        await AssertConstraint(PostgresErrorCodes.UniqueViolation, "IX_Payments_BookingId_IdempotencyKey");
    }

    [PostgresFact]
    public async Task PaymentKey_CanBeReusedForAnotherBookingAndNullReferencesCanRepeat()
    {
        var inventory = await SeedInventory();
        var hold = CreateHold(inventory);
        var first = CreateBooking(hold, "BK-1");
        var second = CreateBooking(hold, "BK-2");
        context.AddRange(first, second,
            PaymentEntity.CreateForBooking(Guid.NewGuid(), first, "same-key", TimeProviderAtNow()),
            PaymentEntity.CreateForBooking(Guid.NewGuid(), second, "same-key", TimeProviderAtNow()));

        await context.SaveChangesAsync();
        Assert.Equal(2, await context.Payments.CountAsync());
    }

    [PostgresFact]
    public async Task ProviderReference_IsUniqueWhenPresent()
    {
        var inventory = await SeedInventory();
        var booking = CreateBooking(CreateHold(inventory));
        var first = PaymentEntity.CreateForBooking(Guid.NewGuid(), booking, "key-1", TimeProviderAtNow());
        first.RecordProviderReference("same-operation");
        context.AddRange(booking, first);
        await context.SaveChangesAsync();
        var second = PaymentEntity.CreateForBooking(Guid.NewGuid(), booking, "key-2", TimeProviderAtNow());
        second.RecordProviderReference("same-operation");
        context.Payments.Add(second);

        await AssertConstraint(PostgresErrorCodes.UniqueViolation, "IX_Payments_ProviderReference");
    }

    [PostgresTheory]
    [InlineData("SeatHold")]
    [InlineData("Booking")]
    public async Task ItemPrimaryKey_RejectsDuplicateSeatsWithinItsParent(string parent)
    {
        var inventory = await SeedInventory();
        var hold = CreateHold(inventory);
        var booking = CreateBooking(hold);
        context.AddRange(hold, booking);
        await context.SaveChangesAsync();
        // Bypass EF's identity map to exercise the actual composite primary keys.
        var exception = await Assert.ThrowsAsync<PostgresException>(() => parent == "SeatHold"
            ? context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"SeatHoldItems\" (\"SeatHoldId\", \"SessionSeatId\", \"PriceSnapshot\", \"Currency\") VALUES ({hold.Id}, {inventory.Inventory.Id}, {10m}, {"UAH"})")
            : context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"BookingItems\" (\"BookingId\", \"SessionSeatId\", \"PriceSnapshot\", \"Currency\") VALUES ({booking.Id}, {inventory.Inventory.Id}, {10m}, {"UAH"})"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal(parent == "SeatHold" ? "PK_SeatHoldItems" : "PK_BookingItems", exception.ConstraintName);
    }

    [PostgresTheory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task HoldItemAllocationVersion_RejectsNonpositiveTokensInPostgres(long version)
    {
        var inventory = await SeedInventory();
        var hold = CreateHold(inventory);
        context.SeatHolds.Add(hold);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Null((await context.SeatHoldItems.SingleAsync()).AllocationVersion);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"SeatHoldItems\" SET \"AllocationVersion\" = {version} WHERE \"SeatHoldId\" = {hold.Id}"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("CK_SeatHoldItems_AllocationVersion_Positive", exception.ConstraintName);
    }

    private async Task AssertConstraint(string sqlState, string constraint)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(sqlState, postgres.SqlState);
        Assert.Equal(constraint, postgres.ConstraintName);
    }

    private async Task<InventoryGraph> SeedInventory()
    {
        var venue = new VenueEntity(Guid.NewGuid(), "Cinema", "Test address");
        var auditorium = new Auditorium(Guid.NewGuid(), venue.Id, "Hall");
        var seat = new Seat(Guid.NewGuid(), auditorium.Id, "A", 1);
        var offering = new Event(Guid.NewGuid(), "Film", "", TimeSpan.FromMinutes(90), 0);
        var session = new Session(Guid.NewGuid(), offering.Id, auditorium.Id, Now.AddHours(2), Now.AddHours(4));
        var inventory = new SessionSeat(Guid.NewGuid(), session.Id, seat.Id, new Money(12.50m, "UAH"));
        context.AddRange(venue, auditorium, seat, offering, session, inventory);
        await context.SaveChangesAsync();
        return new InventoryGraph(venue, auditorium, seat, offering, session, inventory);
    }

    private static SeatHold CreateHold(InventoryGraph inventory) => new(Guid.NewGuid(), inventory.Session.Id, Guid.NewGuid(),
        inventory.Session.StartsAt, [inventory.Inventory], TimeProviderAtNow(), Timing);

    private static BookingEntity CreateBooking(SeatHold hold, string number = "BK-TEST") => BookingEntity.CreateFromHold(
        Guid.NewGuid(), hold, hold.UserId, number, TimeProviderAtNow(), Now.AddHours(2), Timing);

    private static TimeProvider TimeProviderAtNow() => new FixedTimeProvider();

    private sealed class FixedTimeProvider(DateTimeOffset? utcNow = null) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow ?? Now;
    }

    private sealed record InventoryGraph(VenueEntity Venue, Auditorium Auditorium, Seat Seat, Event Event, Session Session, SessionSeat Inventory);

    public sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
            {
                Skip = $"Set {ConnectionVariable} to an isolated PostgreSQL test database.";
            }
        }
    }

    public sealed class PostgresTheoryAttribute : TheoryAttribute
    {
        public PostgresTheoryAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
            {
                Skip = $"Set {ConnectionVariable} to an isolated PostgreSQL test database.";
            }
        }
    }
}
