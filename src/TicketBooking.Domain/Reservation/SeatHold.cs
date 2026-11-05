namespace TicketBooking.Domain.Reservation;

public sealed class SeatHold
{
    public SeatHold(
        Guid id,
        Guid sessionId,
        Guid userId,
        DateTimeOffset expiresAt,
        IEnumerable<SeatHoldItem> items,
        TimeProvider timeProvider)
    {
        Id = DomainGuard.Id(id, nameof(id));
        SessionId = DomainGuard.Id(sessionId, nameof(sessionId));
        UserId = DomainGuard.Id(userId, nameof(userId));
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(timeProvider);
        var now = timeProvider.GetUtcNow().ToUniversalTime();

        if (expiresAt <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "A new hold must expire in the future.");
        }

        var snapshot = items.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(item => item is null || item.SeatHoldId != id))
        {
            throw new ArgumentException("A hold must contain items belonging to this hold.", nameof(items));
        }

        if (snapshot.Select(item => item.SessionSeatId).Distinct().Count() != snapshot.Length)
        {
            throw new ArgumentException("A hold must not contain duplicate session seats.", nameof(items));
        }

        ExpiresAt = expiresAt.ToUniversalTime();
        Items = Array.AsReadOnly(snapshot);
    }

    public Guid Id { get; }
    public Guid SessionId { get; }
    public Guid UserId { get; }
    public DateTimeOffset ExpiresAt { get; }
    public SeatHoldStatus Status { get; private set; } = SeatHoldStatus.Active;
    public IReadOnlyList<SeatHoldItem> Items { get; }

    public void EnsureCanConvert(Guid userId, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        EnsureCanConvertAt(userId, timeProvider.GetUtcNow().ToUniversalTime());
    }

    // Booking creation uses one clock reading for both this check and CreatedAt.
    internal void EnsureCanConvertAt(Guid userId, DateTimeOffset now)
    {
        EnsureOwner(userId);
        EnsureActive();

        if (now >= ExpiresAt)
        {
            throw new InvalidOperationException("The hold has expired.");
        }
    }

    public void Convert(Guid userId, TimeProvider timeProvider)
    {
        EnsureCanConvert(userId, timeProvider);
        Status = SeatHoldStatus.Converted;
    }

    public void Cancel(Guid userId, TimeProvider timeProvider)
    {
        EnsureCanConvert(userId, timeProvider);
        Status = SeatHoldStatus.Cancelled;
    }

    public void Expire(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        EnsureActive();
        var now = timeProvider.GetUtcNow().ToUniversalTime();

        if (now < ExpiresAt)
        {
            throw new InvalidOperationException("The hold has not expired yet.");
        }

        Status = SeatHoldStatus.Expired;
    }

    private void EnsureOwner(Guid userId)
    {
        DomainGuard.Id(userId, nameof(userId));
        if (userId != UserId)
        {
            throw new InvalidOperationException("Only the hold owner can perform this operation.");
        }
    }

    private void EnsureActive()
    {
        if (Status != SeatHoldStatus.Active)
        {
            throw new InvalidOperationException("Only an active hold can change state.");
        }
    }
}
