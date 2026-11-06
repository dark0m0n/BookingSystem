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
        Id = DomainGuard.Id(id, nameof(id));
        UpdateDetails(title, description, duration, ageRating, status);
    }

    public Guid Id { get; private set; }
    public string Title { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public TimeSpan Duration { get; private set; }
    public int AgeRating { get; private set; }
    public EventStatus Status { get; private set; }

    public void UpdateDetails(string title, string description, TimeSpan duration, int ageRating, EventStatus status)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(ageRating);
        ArgumentNullException.ThrowIfNull(description);

        var validatedTitle = DomainGuard.Text(title, nameof(title));
        DomainGuard.Defined(status, nameof(status));

        Title = validatedTitle;
        Description = description;
        Duration = duration;
        AgeRating = ageRating;
        Status = status;
    }
}
