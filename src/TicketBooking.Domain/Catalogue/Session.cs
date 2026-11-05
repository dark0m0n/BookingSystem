namespace TicketBooking.Domain.Catalogue;

public sealed class Session
{
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
        StartsAt = startsAt;
        EndsAt = endsAt;
        Status = DomainGuard.Defined(status, nameof(status));
    }

    public Guid Id { get; }
    public Guid EventId { get; }
    public Guid AuditoriumId { get; }
    public DateTimeOffset StartsAt { get; }
    public DateTimeOffset EndsAt { get; }
    public SessionStatus Status { get; }
}
