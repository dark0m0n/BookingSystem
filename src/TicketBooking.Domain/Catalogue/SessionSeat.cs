namespace TicketBooking.Domain.Catalogue;

public sealed class SessionSeat
{
    // Used by persistence; application callers must use the validated creation API.
    private SessionSeat()
    {
    }

    public SessionSeat(
        Guid id,
        Guid sessionId,
        Guid seatId,
        Money price,
        SessionSeatStatus status = SessionSeatStatus.Available,
        long version = 0)
    {
        ArgumentNullException.ThrowIfNull(price);
        ArgumentOutOfRangeException.ThrowIfNegative(version);

        Id = DomainGuard.Id(id, nameof(id));
        SessionId = DomainGuard.Id(sessionId, nameof(sessionId));
        SeatId = DomainGuard.Id(seatId, nameof(seatId));
        Price = new Money(price.Amount, price.Currency);
        Status = DomainGuard.Defined(status, nameof(status));
        Version = version;
    }

    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid SeatId { get; private set; }
    public Money Price { get; private set; } = null!;
    public SessionSeatStatus Status { get; private set; }
    public long Version { get; private set; }
}
