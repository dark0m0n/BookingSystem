namespace TicketBooking.Domain.Venue;

public sealed class Venue
{
    // Used by persistence; application callers must use the validated creation API.
    private Venue()
    {
    }

    public Venue(Guid id, string name, string address)
    {
        Id = DomainGuard.Id(id, nameof(id));
        Name = DomainGuard.Text(name, nameof(name));
        Address = DomainGuard.Text(address, nameof(address));
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Address { get; private set; } = null!;
}
