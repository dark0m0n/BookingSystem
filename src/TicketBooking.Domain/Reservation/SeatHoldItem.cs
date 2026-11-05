namespace TicketBooking.Domain.Reservation;

public sealed class SeatHoldItem
{
    public SeatHoldItem(Guid seatHoldId, Guid sessionSeatId, decimal priceSnapshot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(priceSnapshot);

        SeatHoldId = DomainGuard.Id(seatHoldId, nameof(seatHoldId));
        SessionSeatId = DomainGuard.Id(sessionSeatId, nameof(sessionSeatId));
        PriceSnapshot = priceSnapshot;
    }

    public Guid SeatHoldId { get; }
    public Guid SessionSeatId { get; }
    public decimal PriceSnapshot { get; }
}
