namespace TicketBooking.Domain;

/// <summary>Validated duration policy; absolute deadlines are calculated and captured at creation.</summary>
public sealed class BookingTimingPolicy
{
    public BookingTimingPolicy(TimeSpan holdDuration, TimeSpan paymentCompletionWindow, TimeSpan cancellationLeadTime)
    {
        if (holdDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(holdDuration), "Hold duration must be positive.");
        }

        if (paymentCompletionWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(paymentCompletionWindow), "Payment window must be positive.");
        }

        if (cancellationLeadTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cancellationLeadTime), "Cancellation lead time must not be negative.");
        }

        HoldDuration = holdDuration;
        PaymentCompletionWindow = paymentCompletionWindow;
        CancellationLeadTime = cancellationLeadTime;
    }

    public TimeSpan HoldDuration { get; }
    public TimeSpan PaymentCompletionWindow { get; }
    public TimeSpan CancellationLeadTime { get; }

    public DateTimeOffset GetHoldExpiration(DateTimeOffset now, DateTimeOffset sessionStartsAt) =>
        CappedDeadline(now, sessionStartsAt, HoldDuration);

    public DateTimeOffset GetPaymentDeadline(DateTimeOffset createdAt, DateTimeOffset sessionStartsAt) =>
        CappedDeadline(createdAt, sessionStartsAt, PaymentCompletionWindow);

    public DateTimeOffset GetCancellationDeadline(DateTimeOffset sessionStartsAt) =>
        sessionStartsAt.ToUniversalTime().Subtract(CancellationLeadTime);

    private static DateTimeOffset CappedDeadline(DateTimeOffset now, DateTimeOffset sessionStartsAt, TimeSpan duration)
    {
        now = now.ToUniversalTime();
        sessionStartsAt = sessionStartsAt.ToUniversalTime();
        if (sessionStartsAt <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionStartsAt), "The session must start in the future.");
        }

        // Compare before adding, so even a very large configured duration is safely capped.
        return duration >= sessionStartsAt - now ? sessionStartsAt : now.Add(duration);
    }
}
