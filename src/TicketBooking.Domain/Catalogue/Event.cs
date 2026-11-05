namespace TicketBooking.Domain.Catalogue;

public sealed class Event
{
    public Event(
        Guid id,
        string title,
        string description,
        TimeSpan duration,
        int ageRating,
        EventStatus status = EventStatus.Draft)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(ageRating);
        ArgumentNullException.ThrowIfNull(description);

        Id = DomainGuard.Id(id, nameof(id));
        Title = DomainGuard.Text(title, nameof(title));
        Description = description;
        Duration = duration;
        AgeRating = ageRating;
        Status = DomainGuard.Defined(status, nameof(status));
    }

    public Guid Id { get; }
    public string Title { get; }
    public string Description { get; }
    public TimeSpan Duration { get; }
    public int AgeRating { get; }
    public EventStatus Status { get; }
}
