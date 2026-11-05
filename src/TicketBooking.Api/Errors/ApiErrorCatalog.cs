using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using TicketBooking.Application.Identity;
using TicketBooking.Contracts.Errors;
using TicketBooking.Domain;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.Api.Errors;

public sealed record ApiErrorDefinition(string Code, int Status, string Title, string Detail);

public static class ApiErrorCatalog
{
    public static ApiErrorDefinition ForException(Exception exception) => exception switch
    {
        AuthenticationRequiredException => ForStatus(401),
        DomainRuleException rule => ForDomainError(rule.Error),
        SeatAllocationConflictException => ForDomainError(DomainError.SeatNotAvailable),
        DbUpdateConcurrencyException conflict when conflict.Entries.Count > 0 &&
            conflict.Entries.All(entry => entry.Entity is SessionSeat) => ForDomainError(DomainError.SeatNotAvailable),
        DbUpdateConcurrencyException => new(ErrorCodes.ConcurrencyConflict, 409, "Concurrency conflict",
            "The resource changed. Refresh its state before trying again."),
        BadHttpRequestException request => ForStatus(request.StatusCode),
        _ => ForStatus(500)
    };

    public static ApiErrorDefinition ForDomainError(DomainError error) => error switch
    {
        DomainError.SeatNotAvailable => new(ErrorCodes.SeatNotAvailable, 409, "Seat not available",
            "The requested seat allocation is no longer available. Refresh the seat map."),
        DomainError.HoldExpired => new(ErrorCodes.HoldExpired, 409, "Hold expired",
            "The seat hold has expired. Create a new hold."),
        DomainError.HoldNotActive => new(ErrorCodes.HoldNotActive, 409, "Hold not active",
            "The seat hold is no longer active."),
        DomainError.HoldNotExpired => new(ErrorCodes.HoldNotExpired, 409, "Hold not expired",
            "The seat hold has not reached its expiration time."),
        DomainError.ResourceNotOwned => new(ErrorCodes.ResourceNotOwned, 403, "Resource access denied",
            "You do not own this resource."),
        DomainError.BookingNotPendingPayment => new(ErrorCodes.BookingNotPendingPayment, 409, "Booking not pending payment",
            "The booking is not awaiting payment."),
        DomainError.BookingNotCancellable => new(ErrorCodes.BookingNotCancellable, 409, "Booking not cancellable",
            "The booking cannot be cancelled in its current state."),
        DomainError.PaymentDeadlinePassed => new(ErrorCodes.PaymentDeadlinePassed, 409, "Payment deadline passed",
            "The booking payment deadline has passed."),
        DomainError.PaymentDeadlineNotReached => new(ErrorCodes.PaymentDeadlineNotReached, 409, "Payment deadline not reached",
            "The booking has not reached its payment deadline."),
        DomainError.CancellationDeadlinePassed => new(ErrorCodes.CancellationDeadlinePassed, 409, "Cancellation deadline passed",
            "The booking cancellation deadline has passed."),
        DomainError.PaymentAlreadyProcessed => new(ErrorCodes.PaymentAlreadyProcessed, 409, "Payment already processed",
            "This payment already has a final outcome."),
        DomainError.PaymentReferenceMismatch => new(ErrorCodes.PaymentReferenceMismatch, 409, "Payment reference mismatch",
            "The provider operation does not match this payment."),
        _ => ForStatus(500)
    };

    public static ApiErrorDefinition ForStatus(int status) => status switch
    {
        400 => new(ErrorCodes.ValidationFailed, 400, "Invalid request", "The request is invalid."),
        401 => new(ErrorCodes.Unauthorized, 401, "Authentication required", "Authentication is required."),
        403 => new(ErrorCodes.Forbidden, 403, "Forbidden", "Access to this resource is forbidden."),
        404 => new(ErrorCodes.NotFound, 404, "Not found", "The requested resource was not found."),
        405 => new(ErrorCodes.MethodNotAllowed, 405, "Method not allowed", "The HTTP method is not allowed for this resource."),
        409 => new(ErrorCodes.Conflict, 409, "Conflict", "The request conflicts with the current resource state."),
        500 => new(ErrorCodes.InternalError, 500, "Internal server error", "An unexpected error occurred."),
        _ => new(ErrorCodes.HttpError, status, ReasonPhrases.GetReasonPhrase(status), "The request could not be completed.")
    };
}
