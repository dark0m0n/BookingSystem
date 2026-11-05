namespace TicketBooking.Domain.Payment;

public enum PaymentStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    // Reserved for the later refund workflow; no transition is exposed yet.
    Refunded = 3
}
