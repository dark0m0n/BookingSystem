namespace TicketBooking.Domain.Venue;

public sealed class Venue
{
    public Venue(Guid id, string name, string address)
    {
        Id = DomainGuard.Id(id, nameof(id));
        Name = DomainGuard.Text(name, nameof(name));
        Address = DomainGuard.Text(address, nameof(address));
    }

    public Guid Id { get; }
    public string Name { get; }
    public string Address { get; }
}
