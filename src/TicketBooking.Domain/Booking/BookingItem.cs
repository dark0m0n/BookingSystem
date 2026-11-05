namespace TicketBooking.Domain.Booking;

public sealed class BookingItem
{
    // Used by persistence; application callers must use the validated creation API.
    private BookingItem()
    {
    }

    public BookingItem(Guid bookingId, Guid sessionSeatId, Money priceSnapshot)
    {
        ArgumentNullException.ThrowIfNull(priceSnapshot);

        BookingId = DomainGuard.Id(bookingId, nameof(bookingId));
        SessionSeatId = DomainGuard.Id(sessionSeatId, nameof(sessionSeatId));
        // Booking history owns a value copy, independent of its source hold item.
        PriceSnapshot = new Money(priceSnapshot.Amount, priceSnapshot.Currency);
    }

    public Guid BookingId { get; private set; }
    public Guid SessionSeatId { get; private set; }
    public Money PriceSnapshot { get; private set; } = null!;
}
