namespace TicketBooking.Domain.Venue;

public sealed class Auditorium
{
    // Used by persistence; application callers must use the validated creation API.
    private Auditorium()
    {
    }

    public Auditorium(Guid id, Guid venueId, string name)
    {
        Id = DomainGuard.Id(id, nameof(id));
        VenueId = DomainGuard.Id(venueId, nameof(venueId));
        Name = DomainGuard.Text(name, nameof(name));
    }

    public Guid Id { get; private set; }
    public Guid VenueId { get; private set; }
    public string Name { get; private set; } = null!;

    public void Rename(string name)
    {
        Name = DomainGuard.Text(name, nameof(name));
    }
}
