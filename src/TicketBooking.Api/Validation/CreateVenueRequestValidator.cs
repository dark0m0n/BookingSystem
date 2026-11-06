using FluentValidation;
using TicketBooking.Contracts.Administration;

namespace TicketBooking.Api.Validation;

public sealed class CreateVenueRequestValidator : AbstractValidator<CreateVenueRequest>
{
    public CreateVenueRequestValidator()
    {
        RuleFor(x => x.Name).Must(x => RequestRules.Text(x, 200)).WithMessage("A name of at most 200 characters is required.").OverridePropertyName("name");
        RuleFor(x => x.Address).Must(x => RequestRules.Text(x, 500)).WithMessage("An address of at most 500 characters is required.").OverridePropertyName("address");
    }
}
