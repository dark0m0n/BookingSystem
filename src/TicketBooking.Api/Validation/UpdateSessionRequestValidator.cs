using FluentValidation;
using TicketBooking.Contracts.Administration;

namespace TicketBooking.Api.Validation;

public sealed class UpdateSessionRequestValidator : AbstractValidator<UpdateSessionRequest>
{
    public UpdateSessionRequestValidator(TimeProvider clock)
    {
        RuleFor(x => x).Must(x => x is not { EventId: null, AuditoriumId: null, StartsAt: null })
            .WithMessage("Supply at least one session field to update.").OverridePropertyName("session");
        RuleFor(x => x.EventId).Must(x => x != Guid.Empty).OverridePropertyName("eventId");
        RuleFor(x => x.AuditoriumId).Must(x => x != Guid.Empty).OverridePropertyName("auditoriumId");
        RuleFor(x => x.StartsAt).Must(x => RequestRules.Future(x, clock)).When(x => x.StartsAt is not null)
            .WithMessage("Session start must be in the future.").OverridePropertyName("startsAt");
    }
}
