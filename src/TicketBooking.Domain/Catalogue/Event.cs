namespace TicketBooking.Domain.Catalogue;

public sealed class Event
{
    // Used by persistence; application callers must use the validated creation API.
    private Event()
    {
    }

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

    public Guid Id { get; private set; }
    public string Title { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public TimeSpan Duration { get; private set; }
    public int AgeRating { get; private set; }
    public EventStatus Status { get; private set; }
}
