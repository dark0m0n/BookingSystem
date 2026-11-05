namespace TicketBooking.Domain;

public enum DomainError
{
    SeatNotAvailable = 0,
    HoldExpired = 1,
    HoldNotActive = 2,
    HoldNotExpired = 3,
    ResourceNotOwned = 4,
    BookingNotPendingPayment = 5,
    BookingNotCancellable = 6,
    PaymentDeadlinePassed = 7,
    PaymentDeadlineNotReached = 8,
    CancellationDeadlinePassed = 9,
    PaymentAlreadyProcessed = 10,
    PaymentReferenceMismatch = 11
}
