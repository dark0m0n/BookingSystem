namespace TicketBooking.Domain.Venue;

public sealed class Seat
{
    public Seat(Guid id, Guid auditoriumId, string row, int number, SeatType type = SeatType.Standard)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);

        Id = DomainGuard.Id(id, nameof(id));
        AuditoriumId = DomainGuard.Id(auditoriumId, nameof(auditoriumId));
        Row = DomainGuard.Text(row, nameof(row));
        Number = number;
        Type = DomainGuard.Defined(type, nameof(type));
    }

    public Guid Id { get; }
    public Guid AuditoriumId { get; }
    public string Row { get; }
    public int Number { get; }
    public SeatType Type { get; }
}
