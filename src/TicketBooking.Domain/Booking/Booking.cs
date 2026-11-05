using TicketBooking.Domain.Identity;
using TicketBooking.Domain.Reservation;

namespace TicketBooking.Domain.Booking;

public sealed class Booking
{
    // Used by persistence; application callers must use the validated creation API.
    private Booking()
    {
    }

    private List<BookingItem> items = [];

    private Booking(
        Guid id,
        Guid userId,
        Guid sessionId,
        string number,
        IReadOnlyList<BookingItem> items,
        DateTimeOffset createdAt,
        DateTimeOffset sessionStartsAt,
        BookingTimingPolicy timingPolicy)
    {
        Id = DomainGuard.Id(id, nameof(id));
        UserId = userId;
        SessionId = sessionId;
        Number = DomainGuard.Text(number, nameof(number));
        this.items = items.ToList();
        TotalAmount = Money.Sum(items.Select(item => item.PriceSnapshot));
        CreatedAt = createdAt.ToUniversalTime();
        SessionStartsAt = sessionStartsAt.ToUniversalTime();
        PaymentDeadline = timingPolicy.GetPaymentDeadline(CreatedAt, SessionStartsAt);
        CancellationDeadline = timingPolicy.GetCancellationDeadline(SessionStartsAt);
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid SessionId { get; private set; }
    public string Number { get; private set; } = null!;
    public BookingStatus Status { get; private set; } = BookingStatus.PendingPayment;
    public Money TotalAmount { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset SessionStartsAt { get; private set; }
    public DateTimeOffset PaymentDeadline { get; private set; }
    public DateTimeOffset CancellationDeadline { get; private set; }
    public IReadOnlyList<BookingItem> Items => items.AsReadOnly();

    /// <summary>
    /// Copies an owner's valid hold without mutating it. Application must persist the booking
    /// and convert the hold in the same transaction, preventing multiple bookings from one hold.
    /// </summary>
    public static Booking CreateFromHold(
        Guid id,
        SeatHold hold,
        Guid userId,
        string number,
        TimeProvider timeProvider,
        DateTimeOffset sessionStartsAt,
        BookingTimingPolicy timingPolicy)
    {
        ArgumentNullException.ThrowIfNull(hold);
        ArgumentNullException.ThrowIfNull(timingPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        var now = timeProvider.GetUtcNow().ToUniversalTime();
        hold.EnsureCanConvertAt(userId, now);

        var items = hold.Items
            .Select(item => new BookingItem(id, item.SessionSeatId, item.PriceSnapshot))
            .ToArray();

        // Application must coordinate persisting the booking and converting the hold atomically.
        return new Booking(id, userId, hold.SessionId, number, Array.AsReadOnly(items), now, sessionStartsAt, timingPolicy);
    }

    /// <summary>
    /// Confirms a pending booking after Application has validated payment and seat allocation.
    /// </summary>
    public void Confirm(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        EnsurePaymentCanCompleteAt(timeProvider.GetUtcNow().ToUniversalTime());
        // Application must validate successful payment and commit the seat changes atomically.
        Status = BookingStatus.Confirmed;
    }

    /// <summary>
    /// Enforces ownership and the cancellation deadline captured at booking creation.
    /// </summary>
    public void Cancel(Guid userId, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var now = timeProvider.GetUtcNow().ToUniversalTime();
        ResourceOwnership.EnsureOwner(userId, UserId);

        if (Status is not (BookingStatus.PendingPayment or BookingStatus.Confirmed))
        {
            throw new DomainRuleException(DomainError.BookingNotCancellable, "This booking cannot be cancelled.");
        }

        if (now >= CancellationDeadline)
        {
            throw new DomainRuleException(DomainError.CancellationDeadlinePassed, "The cancellation deadline has passed.");
        }

        Status = BookingStatus.Cancelled;
    }

    /// <summary>
    /// Expires a pending booking when its captured payment deadline is reached.
    /// </summary>
    public void Expire(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        EnsurePendingPayment();
        var now = timeProvider.GetUtcNow().ToUniversalTime();
        if (now < PaymentDeadline)
        {
            throw new DomainRuleException(DomainError.PaymentDeadlineNotReached, "The payment deadline has not passed.");
        }

        Status = BookingStatus.Expired;
    }

    internal void EnsurePaymentCanCompleteAt(DateTimeOffset now)
    {
        EnsurePendingPayment();
        if (now >= PaymentDeadline)
        {
            throw new DomainRuleException(DomainError.PaymentDeadlinePassed, "The payment completion deadline has passed.");
        }
    }

    private void EnsurePendingPayment()
    {
        if (Status != BookingStatus.PendingPayment)
        {
            throw new DomainRuleException(DomainError.BookingNotPendingPayment, "Only a booking pending payment can perform this operation.");
        }
    }
}
