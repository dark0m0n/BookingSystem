using FluentValidation;
using TicketBooking.Contracts.Administration;

namespace TicketBooking.Api.Validation;

public sealed class UpdateAuditoriumRequestValidator : AbstractValidator<UpdateAuditoriumRequest>
{
    public UpdateAuditoriumRequestValidator() =>
        RuleFor(x => x.Name).Must(x => RequestRules.Text(x, 200)).WithMessage("A name of at most 200 characters is required.").OverridePropertyName("name");
}
