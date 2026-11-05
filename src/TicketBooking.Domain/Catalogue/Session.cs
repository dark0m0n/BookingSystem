namespace TicketBooking.Domain.Catalogue;

public sealed class Session
{
    // Used by persistence; application callers must use the validated creation API.
    private Session()
    {
    }

    public Session(
        Guid id,
        Guid eventId,
        Guid auditoriumId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        SessionStatus status = SessionStatus.Scheduled)
    {
        if (endsAt <= startsAt)
        {
            throw new ArgumentOutOfRangeException(nameof(endsAt), "End time must be after start time.");
        }

        Id = DomainGuard.Id(id, nameof(id));
        EventId = DomainGuard.Id(eventId, nameof(eventId));
        AuditoriumId = DomainGuard.Id(auditoriumId, nameof(auditoriumId));
        StartsAt = startsAt.ToUniversalTime();
        EndsAt = endsAt.ToUniversalTime();
        Status = DomainGuard.Defined(status, nameof(status));
    }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid AuditoriumId { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public SessionStatus Status { get; private set; }
}
