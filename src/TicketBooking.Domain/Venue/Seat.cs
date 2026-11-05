namespace TicketBooking.Domain.Venue;

public sealed class Seat
{
    // Used by persistence; application callers must use the validated creation API.
    private Seat()
    {
    }

    public Seat(Guid id, Guid auditoriumId, string row, int number, SeatType type = SeatType.Standard)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);

        Id = DomainGuard.Id(id, nameof(id));
        AuditoriumId = DomainGuard.Id(auditoriumId, nameof(auditoriumId));
        Row = DomainGuard.Text(row, nameof(row));
        Number = number;
        Type = DomainGuard.Defined(type, nameof(type));
    }

    public Guid Id { get; private set; }
    public Guid AuditoriumId { get; private set; }
    public string Row { get; private set; } = null!;
    public int Number { get; private set; }
    public SeatType Type { get; private set; }
}
