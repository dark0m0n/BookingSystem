using TicketBooking.Application.Identity;
using TicketBooking.Domain;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;
using BookingEntity = TicketBooking.Domain.Booking.Booking;
using PaymentEntity = TicketBooking.Domain.Payment.Payment;

namespace TicketBooking.Application.Tests;

public class ResourceOwnershipServiceTests
{
    private readonly SeatHold hold;
    private readonly BookingEntity booking;
    private readonly PaymentEntity payment;

    public ResourceOwnershipServiceTests()
    {
        var clock = new FixedClock();
        var startsAt = clock.GetUtcNow().AddHours(2);
        var timing = new BookingTimingPolicy(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1));
        var sessionId = Guid.NewGuid();
        var seat = new SessionSeat(Guid.NewGuid(), sessionId, Guid.NewGuid(), new Money(100m, "UAH"));
        hold = new SeatHold(Guid.NewGuid(), sessionId, Guid.NewGuid(), startsAt, [seat], clock, timing);
        booking = BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "TEST-1", clock, startsAt, timing);
        payment = PaymentEntity.CreateForBooking(Guid.NewGuid(), booking, "test-request", clock);
    }

    [Theory]
    [InlineData("hold")]
    [InlineData("booking")]
    [InlineData("payment")]
    [InlineData("owner-id")]
    public void Owner_CanAccessWithoutChangingResourceState(string resource)
    {
        Check(new ResourceOwnershipService(new CurrentUser(hold.UserId)), resource);

        AssertUnchanged();
    }

    [Theory]
    [InlineData("hold")]
    [InlineData("booking")]
    [InlineData("payment")]
    [InlineData("owner-id")]
    public void AnotherUser_IsDeniedWithoutChangingResourceState(string resource)
    {
        var service = new ResourceOwnershipService(new CurrentUser(Guid.NewGuid()));

        var error = Assert.Throws<DomainRuleException>(() => Check(service, resource));

        Assert.Equal(DomainError.ResourceNotOwned, error.Error);
        AssertUnchanged();
    }

    [Theory]
    [InlineData("hold", false)]
    [InlineData("booking", false)]
    [InlineData("payment", false)]
    [InlineData("owner-id", false)]
    [InlineData("hold", true)]
    [InlineData("booking", true)]
    [InlineData("payment", true)]
    [InlineData("owner-id", true)]
    public void MissingOrEmptyCaller_RequiresAuthentication(string resource, bool emptyId)
    {
        var service = new ResourceOwnershipService(new CurrentUser(emptyId ? Guid.Empty : null));

        Assert.Throws<AuthenticationRequiredException>(() => Check(service, resource));

        AssertUnchanged();
    }

    [Fact]
    public void Payment_CannotBeAuthorizedWithAnotherBookingEvenForTheSameOwner()
    {
        var clock = new FixedClock();
        var timing = new BookingTimingPolicy(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1));
        var otherBooking = BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "TEST-2", clock,
            clock.GetUtcNow().AddHours(2), timing);
        var service = new ResourceOwnershipService(new CurrentUser(hold.UserId));

        var error = Assert.Throws<DomainRuleException>(() => service.EnsurePaymentOwner(payment, otherBooking));

        Assert.Equal(DomainError.ResourceNotOwned, error.Error);
        AssertUnchanged();
    }

    [Fact]
    public void Ownership_DoesNotImplyLifecycleEligibility()
    {
        hold.Cancel(hold.UserId, new FixedClock());
        var service = new ResourceOwnershipService(new CurrentUser(hold.UserId));

        service.EnsureHoldOwner(hold);

        Assert.Equal(DomainError.HoldNotActive,
            Assert.Throws<DomainRuleException>(() => hold.Convert(hold.UserId, new FixedClock())).Error);
        Assert.Equal(SeatHoldStatus.Cancelled, hold.Status);
    }

    private void Check(ResourceOwnershipService service, string resource)
    {
        switch (resource)
        {
            case "hold": service.EnsureHoldOwner(hold); break;
            case "booking": service.EnsureBookingOwner(booking); break;
            case "payment": service.EnsurePaymentOwner(payment, booking); break;
            case "owner-id": service.EnsureOwner(booking.UserId); break;
            default: throw new ArgumentOutOfRangeException(nameof(resource));
        }
    }

    private void AssertUnchanged()
    {
        Assert.Equal(SeatHoldStatus.Active, hold.Status);
        Assert.Equal(TicketBooking.Domain.Booking.BookingStatus.PendingPayment, booking.Status);
        Assert.Equal(TicketBooking.Domain.Payment.PaymentStatus.Pending, payment.Status);
        Assert.Null(payment.ProviderReference);
        Assert.Equal(new Money(100m, "UAH"), payment.Amount);
    }

    private sealed record CurrentUser(Guid? UserId) : ICurrentUser;

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);
    }
}
