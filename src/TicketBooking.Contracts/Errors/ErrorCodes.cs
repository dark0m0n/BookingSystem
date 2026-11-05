namespace TicketBooking.Contracts.Errors;

/// <summary>Stable wire values. Do not rename values or derive them from exception messages.</summary>
public static class ErrorCodes
{
    public const string AccountAlreadyExists = "account_already_exists";
    public const string InvalidCredentials = "invalid_credentials";
    public const string InvalidRefreshToken = "invalid_refresh_token";
    public const string SeatNotAvailable = "seat_not_available";
    public const string HoldExpired = "hold_expired";
    public const string HoldNotActive = "hold_not_active";
    public const string HoldNotExpired = "hold_not_expired";
    public const string ResourceNotOwned = "resource_not_owned";
    public const string BookingNotPendingPayment = "booking_not_pending_payment";
    public const string BookingNotCancellable = "booking_not_cancellable";
    public const string PaymentDeadlinePassed = "payment_deadline_passed";
    public const string PaymentDeadlineNotReached = "payment_deadline_not_reached";
    public const string CancellationDeadlinePassed = "cancellation_deadline_passed";
    public const string PaymentAlreadyProcessed = "payment_already_processed";
    public const string PaymentReferenceMismatch = "payment_reference_mismatch";
    public const string ValidationFailed = "validation_failed";
    public const string ConcurrencyConflict = "concurrency_conflict";
    public const string NotFound = "not_found";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string MethodNotAllowed = "method_not_allowed";
    public const string Conflict = "conflict";
    public const string HttpError = "http_error";
    public const string InternalError = "internal_error";
}
