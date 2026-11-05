namespace TicketBooking.Domain.Booking;

public enum BookingStatus
{
    PendingPayment = 0,
    Confirmed = 1,
    Cancelled = 2,
    Expired = 3,
    // Reserved for the later refund workflow; no transition is exposed yet.
    Refunded = 4
}
