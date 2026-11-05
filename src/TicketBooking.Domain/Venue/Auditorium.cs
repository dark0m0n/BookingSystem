namespace TicketBooking.Domain.Venue;

public sealed class Auditorium
{
    public Auditorium(Guid id, Guid venueId, string name)
    {
        Id = DomainGuard.Id(id, nameof(id));
        VenueId = DomainGuard.Id(venueId, nameof(venueId));
        Name = DomainGuard.Text(name, nameof(name));
    }

    public Guid Id { get; }
    public Guid VenueId { get; }
    public string Name { get; }
}
