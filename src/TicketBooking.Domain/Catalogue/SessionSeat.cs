namespace TicketBooking.Domain.Catalogue;

public sealed class SessionSeat
{
    public SessionSeat(
        Guid id,
        Guid sessionId,
        Guid seatId,
        decimal price,
        SessionSeatStatus status = SessionSeatStatus.Available,
        long version = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(price);
        ArgumentOutOfRangeException.ThrowIfNegative(version);

        Id = DomainGuard.Id(id, nameof(id));
        SessionId = DomainGuard.Id(sessionId, nameof(sessionId));
        SeatId = DomainGuard.Id(seatId, nameof(seatId));
        Price = price;
        Status = DomainGuard.Defined(status, nameof(status));
        Version = version;
    }

    public Guid Id { get; }
    public Guid SessionId { get; }
    public Guid SeatId { get; }
    public decimal Price { get; }
    public SessionSeatStatus Status { get; }
    public long Version { get; }
}
