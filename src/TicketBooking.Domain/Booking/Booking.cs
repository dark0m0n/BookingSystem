using TicketBooking.Domain.Reservation;

namespace TicketBooking.Domain.Booking;

public sealed class Booking
{
    private Booking(
        Guid id,
        Guid userId,
        Guid sessionId,
        string number,
        IReadOnlyList<BookingItem> items,
        DateTimeOffset createdAt)
    {
        Id = DomainGuard.Id(id, nameof(id));
        UserId = userId;
        SessionId = sessionId;
        Number = DomainGuard.Text(number, nameof(number));
        Items = items;
        TotalAmount = items.Sum(item => item.PriceSnapshot);
        CreatedAt = createdAt.ToUniversalTime();
    }

    public Guid Id { get; }
    public Guid UserId { get; }
    public Guid SessionId { get; }
    public string Number { get; }
    public BookingStatus Status { get; private set; } = BookingStatus.PendingPayment;
    public decimal TotalAmount { get; }
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyList<BookingItem> Items { get; }

    /// <summary>
    /// Copies an owner's valid hold without mutating it. Application must persist the booking
    /// and convert the hold in the same transaction, preventing multiple bookings from one hold.
    /// </summary>
    public static Booking CreateFromHold(
        Guid id,
        SeatHold hold,
        Guid userId,
        string number,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(hold);
        ArgumentNullException.ThrowIfNull(timeProvider);
        var now = timeProvider.GetUtcNow().ToUniversalTime();
        hold.EnsureCanConvertAt(userId, now);

        var items = hold.Items
            .Select(item => new BookingItem(id, item.SessionSeatId, item.PriceSnapshot))
            .ToArray();

        // Application must coordinate persisting the booking and converting the hold atomically.
        return new Booking(id, userId, hold.SessionId, number, Array.AsReadOnly(items), now);
    }

    /// <summary>
    /// Confirms a pending booking after Application has validated payment and seat allocation.
    /// </summary>
    public void Confirm()
    {
        EnsurePendingPayment();
        // Application must validate successful payment and commit the seat changes atomically.
        Status = BookingStatus.Confirmed;
    }

    /// <summary>
    /// Enforces ownership and a deadline derived by Application from the authoritative session.
    /// No default cancellation window is assumed.
    /// </summary>
    public void Cancel(
        Guid userId,
        TimeProvider timeProvider,
        DateTimeOffset sessionStartsAt,
        DateTimeOffset cancellationDeadline)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var now = timeProvider.GetUtcNow().ToUniversalTime();
        sessionStartsAt = sessionStartsAt.ToUniversalTime();
        cancellationDeadline = cancellationDeadline.ToUniversalTime();
        DomainGuard.Id(userId, nameof(userId));
        if (userId != UserId)
        {
            throw new InvalidOperationException("Only the booking owner can cancel it.");
        }

        if (cancellationDeadline > sessionStartsAt)
        {
            throw new ArgumentOutOfRangeException(nameof(cancellationDeadline),
                "The cancellation deadline must not be after the session starts.");
        }

        if (Status is not (BookingStatus.PendingPayment or BookingStatus.Confirmed))
        {
            throw new InvalidOperationException("This booking cannot be cancelled.");
        }

        if (now >= cancellationDeadline)
        {
            throw new InvalidOperationException("The cancellation deadline has passed.");
        }

        Status = BookingStatus.Cancelled;
    }

    /// <summary>
    /// Expires a pending booking when the authoritative payment deadline supplied by Application is reached.
    /// </summary>
    public void Expire(TimeProvider timeProvider, DateTimeOffset paymentDeadline)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        EnsurePendingPayment();
        var now = timeProvider.GetUtcNow().ToUniversalTime();
        paymentDeadline = paymentDeadline.ToUniversalTime();
        if (paymentDeadline <= CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(paymentDeadline),
                "The payment deadline must be after booking creation.");
        }

        if (now < paymentDeadline)
        {
            throw new InvalidOperationException("The payment deadline has not passed.");
        }

        Status = BookingStatus.Expired;
    }

    private void EnsurePendingPayment()
    {
        if (Status != BookingStatus.PendingPayment)
        {
            throw new InvalidOperationException("Only a booking pending payment can perform this operation.");
        }
    }
}
