namespace TicketBooking.Domain.Booking;

public sealed class BookingItem
{
    public BookingItem(Guid bookingId, Guid sessionSeatId, decimal priceSnapshot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(priceSnapshot);

        BookingId = DomainGuard.Id(bookingId, nameof(bookingId));
        SessionSeatId = DomainGuard.Id(sessionSeatId, nameof(sessionSeatId));
        PriceSnapshot = priceSnapshot;
    }

    public Guid BookingId { get; }
    public Guid SessionSeatId { get; }
    public decimal PriceSnapshot { get; }
}
