using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
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

public class PersistenceModelTests
{
    [Fact]
    public void Model_ContainsAllEntitiesWithRequiredRestrictiveForeignKeys()
    {
        using var context = CreateContext();
        var model = context.Model;

        var domainEntities = model.GetEntityTypes()
            .Where(entity => entity.ClrType.Assembly == typeof(SessionSeat).Assembly).ToArray();
        Assert.Equal(11, domainEntities.Length);
        var foreignKeys = domainEntities.SelectMany(entity => entity.GetForeignKeys()).ToArray();
        Assert.Equal(13, foreignKeys.Length);
        Assert.All(foreignKeys, key =>
        {
            Assert.True(key.IsRequired);
            Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior);
        });
    }

    [Theory]
    [InlineData(typeof(Auditorium), "VenueId", typeof(VenueEntity))]
    [InlineData(typeof(Seat), "AuditoriumId", typeof(Auditorium))]
    [InlineData(typeof(Session), "EventId", typeof(Event))]
    [InlineData(typeof(Session), "AuditoriumId", typeof(Auditorium))]
    [InlineData(typeof(SessionSeat), "SessionId", typeof(Session))]
    [InlineData(typeof(SessionSeat), "SeatId", typeof(Seat))]
    [InlineData(typeof(SeatHold), "SessionId", typeof(Session))]
    [InlineData(typeof(SeatHoldItem), "SeatHoldId", typeof(SeatHold))]
    [InlineData(typeof(SeatHoldItem), "SessionSeatId", typeof(SessionSeat))]
    [InlineData(typeof(BookingEntity), "SessionId", typeof(Session))]
    [InlineData(typeof(BookingItem), "BookingId", typeof(BookingEntity))]
    [InlineData(typeof(BookingItem), "SessionSeatId", typeof(SessionSeat))]
    [InlineData(typeof(PaymentEntity), "BookingId", typeof(BookingEntity))]
    public void ForeignKey_TargetsTheExpectedParent(Type child, string property, Type parent)
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(child)!;

        Assert.Contains(entity.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == parent &&
            key.Properties.Select(item => item.Name).SequenceEqual([property]));
    }

    [Fact]
    public void UniqueIndexes_DefineInventoryAndPaymentIdentity()
    {
        using var context = CreateContext();
        var model = context.Model;

        AssertUnique(model, typeof(Seat), "AuditoriumId", "Row", "Number");
        AssertUnique(model, typeof(SessionSeat), "SessionId", "SeatId");
        AssertUnique(model, typeof(BookingEntity), "Number");
        AssertUnique(model, typeof(PaymentEntity), "BookingId", "IdempotencyKey");
        AssertUnique(model, typeof(PaymentEntity), "ProviderReference");
        Assert.Equal("\"ProviderReference\" IS NOT NULL", model.FindEntityType(typeof(PaymentEntity))!
            .GetIndexes().Single(index => index.Properties.Count == 1 && index.Properties[0].Name == "ProviderReference").GetFilter());
    }

    [Theory]
    [InlineData(typeof(SeatHoldItem), "SeatHoldId")]
    [InlineData(typeof(BookingItem), "BookingId")]
    public void ItemKey_AllowsHistoryAcrossParentsButNotDuplicateSeatsWithinOneParent(Type type, string parentKey)
    {
        using var context = CreateContext();

        Assert.Equal(new[] { parentKey, "SessionSeatId" }, context.Model.FindEntityType(type)!
            .FindPrimaryKey()!.Properties.Select(property => property.Name));
    }

    [Fact]
    public void Money_UsesRequiredComplexColumnsWithExplicitPrecision()
    {
        using var context = CreateContext();
        var values = context.Model.GetEntityTypes().SelectMany(entity => entity.GetComplexProperties()).ToArray();

        Assert.Equal(6, values.Length);
        Assert.All(values, value =>
        {
            Assert.False(value.IsNullable);
            Assert.Equal(Money.Precision, value.ComplexType.FindProperty(nameof(Money.Amount))!.GetPrecision());
            Assert.Equal(Money.Scale, value.ComplexType.FindProperty(nameof(Money.Amount))!.GetScale());
            Assert.False(value.ComplexType.FindProperty(nameof(Money.Currency))!.IsNullable);
            Assert.Equal(3, value.ComplexType.FindProperty(nameof(Money.Currency))!.GetMaxLength());
        });
    }

    [Fact]
    public void SessionSeatVersion_IsAnApplicationManagedConcurrencyToken()
    {
        using var context = CreateContext();
        var version = context.Model.FindEntityType(typeof(SessionSeat))!.FindProperty(nameof(SessionSeat.Version))!;

        Assert.True(version.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.Never, version.ValueGenerated);
    }

    private static void AssertUnique(IModel model, Type type, params string[] properties) =>
        Assert.Contains(model.FindEntityType(type)!.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(properties));

    internal static TicketBookingDbContext CreateContext(string? connectionString = null) => new(
        new DbContextOptionsBuilder<TicketBookingDbContext>()
            .UseNpgsql(connectionString ?? "Host=localhost;Database=metadata_only;Username=test")
            .Options);
}
