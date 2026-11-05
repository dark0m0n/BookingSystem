using TicketBooking.Domain;
using TicketBooking.Domain.Identity;
using TicketBooking.Domain.Reservation;
using BookingEntity = TicketBooking.Domain.Booking.Booking;
using PaymentEntity = TicketBooking.Domain.Payment.Payment;

namespace TicketBooking.Application.Identity;

/// <summary>
/// Authorizes customer access to server-loaded resources. Role policies and lifecycle rules are separate checks.
/// </summary>
public sealed class ResourceOwnershipService(ICurrentUser currentUser)
{
    public void EnsureOwner(Guid ownerId) => ResourceOwnership.EnsureOwner(RequireUserId(), ownerId);

    public void EnsureHoldOwner(SeatHold hold)
    {
        ArgumentNullException.ThrowIfNull(hold);
        EnsureOwner(hold.UserId);
    }

    public void EnsureBookingOwner(BookingEntity booking)
    {
        ArgumentNullException.ThrowIfNull(booking);
        EnsureOwner(booking.UserId);
    }

    public void EnsurePaymentOwner(PaymentEntity payment, BookingEntity booking)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(booking);
        var userId = RequireUserId();
        // A caller-owned booking cannot authorize a payment belonging to another booking.
        if (payment.BookingId != booking.Id)
        {
            throw new DomainRuleException(DomainError.ResourceNotOwned, "The resource ownership chain does not match.");
        }
        ResourceOwnership.EnsureOwner(userId, booking.UserId);
    }

    private Guid RequireUserId() => currentUser.UserId is { } id && id != Guid.Empty
        ? id
        : throw new AuthenticationRequiredException();
}
