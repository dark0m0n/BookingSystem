using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Identity;

namespace TicketBooking.Domain.Reservation;

public sealed class SeatHold
{
    // Used by persistence; application callers must use the validated creation API.
    private SeatHold()
    {
    }

    private List<SeatHoldItem> items = [];

    public SeatHold(
        Guid id,
        Guid sessionId,
        Guid userId,
        DateTimeOffset sessionStartsAt,
        IEnumerable<SessionSeat> seats,
        TimeProvider timeProvider,
        BookingTimingPolicy timingPolicy)
    {
        Id = DomainGuard.Id(id, nameof(id));
        SessionId = DomainGuard.Id(sessionId, nameof(sessionId));
        UserId = DomainGuard.Id(userId, nameof(userId));
        ArgumentNullException.ThrowIfNull(seats);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(timingPolicy);
        var now = timeProvider.GetUtcNow().ToUniversalTime();

        var expiresAt = timingPolicy.GetHoldExpiration(now, sessionStartsAt);

        var selectedSeats = seats.ToArray();
        if (selectedSeats.Length == 0 || selectedSeats.Any(seat => seat is null || seat.SessionId != sessionId))
        {
            throw new ArgumentException("A hold must contain seats from its session.", nameof(seats));
        }

        if (selectedSeats.Select(seat => seat.Id).Distinct().Count() != selectedSeats.Length)
        {
            throw new ArgumentException("A hold must not contain duplicate session seats.", nameof(seats));
        }

        if (selectedSeats.Any(seat => seat.Status != SessionSeatStatus.Available))
        {
            throw new DomainRuleException(DomainError.SeatNotAvailable, "Only available session seats can be held.");
        }

        // Prices come from server-resolved inventory, not request price snapshots or totals.
        var snapshot = selectedSeats.Select(seat => new SeatHoldItem(id, seat.Id, seat.Price)).ToArray();
        TotalAmount = Money.Sum(snapshot.Select(item => item.PriceSnapshot));
        ExpiresAt = expiresAt.ToUniversalTime();
        items = snapshot.ToList();
    }

    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public SeatHoldStatus Status { get; private set; } = SeatHoldStatus.Active;
    public IReadOnlyList<SeatHoldItem> Items => items.AsReadOnly();
    public Money TotalAmount { get; private set; } = null!;

    public void EnsureCanConvert(Guid userId, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        EnsureCanConvertAt(userId, timeProvider.GetUtcNow().ToUniversalTime());
    }

    // Booking creation uses one clock reading for both this check and CreatedAt.
    internal void EnsureCanConvertAt(Guid userId, DateTimeOffset now)
    {
        ResourceOwnership.EnsureOwner(userId, UserId);
        EnsureActive();

        if (now >= ExpiresAt)
        {
            throw new DomainRuleException(DomainError.HoldExpired, "The hold has expired.");
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
            throw new DomainRuleException(DomainError.HoldNotExpired, "The hold has not expired yet.");
        }

        Status = SeatHoldStatus.Expired;
    }

    private void EnsureActive()
    {
        if (Status == SeatHoldStatus.Expired)
        {
            throw new DomainRuleException(DomainError.HoldExpired, "The hold has expired.");
        }

        if (Status != SeatHoldStatus.Active)
        {
            throw new DomainRuleException(DomainError.HoldNotActive, "Only an active hold can change state.");
        }
    }
}
