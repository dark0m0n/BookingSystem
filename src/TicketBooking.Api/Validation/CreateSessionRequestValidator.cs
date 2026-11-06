using FluentValidation;
using TicketBooking.Contracts.Administration;

namespace TicketBooking.Api.Validation;

public sealed class CreateSessionRequestValidator : AbstractValidator<CreateSessionRequest>
{
    public CreateSessionRequestValidator(TimeProvider clock)
    {
        RuleFor(x => x.EventId).NotEmpty().OverridePropertyName("eventId");
        RuleFor(x => x.AuditoriumId).NotEmpty().OverridePropertyName("auditoriumId");
        RuleFor(x => x.StartsAt).Must(x => RequestRules.Future(x, clock)).WithMessage("Session start must be in the future.").OverridePropertyName("startsAt");
        RuleFor(x => x).Must(RequestRules.Price).WithMessage("Supply a nonnegative decimal(18,2) price and UAH, EUR or USD currency.").OverridePropertyName("price");
    }
}
