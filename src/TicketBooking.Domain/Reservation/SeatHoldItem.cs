namespace TicketBooking.Domain.Reservation;

public sealed class SeatHoldItem
{
    // Used by persistence; application callers must use the validated creation API.
    private SeatHoldItem()
    {
    }

    public SeatHoldItem(Guid seatHoldId, Guid sessionSeatId, Money priceSnapshot)
    {
        ArgumentNullException.ThrowIfNull(priceSnapshot);

        SeatHoldId = DomainGuard.Id(seatHoldId, nameof(seatHoldId));
        SessionSeatId = DomainGuard.Id(sessionSeatId, nameof(sessionSeatId));
        // EF can materialize/change private setters on complex values. Do not share the
        // catalogue's Money instance, even though it is read-only to normal callers.
        PriceSnapshot = new Money(priceSnapshot.Amount, priceSnapshot.Currency);
    }

    public Guid SeatHoldId { get; private set; }
    public Guid SessionSeatId { get; private set; }
    public Money PriceSnapshot { get; private set; } = null!;
    public long? AllocationVersion { get; private set; }

    public void CaptureAllocationVersion(long version)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        if (AllocationVersion is not null)
        {
            throw new InvalidOperationException("The allocation version has already been captured.");
        }

        AllocationVersion = version;
    }
}
