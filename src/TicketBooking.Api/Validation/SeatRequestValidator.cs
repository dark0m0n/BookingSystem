using FluentValidation;
using TicketBooking.Contracts.Administration;
using TicketBooking.Domain.Venue;

namespace TicketBooking.Api.Validation;

public sealed class SeatRequestValidator : AbstractValidator<SeatRequest>
{
    public SeatRequestValidator()
    {
        RuleFor(x => x.Row).Must(x => RequestRules.Text(x, 20)).WithMessage("A row of at most 20 characters is required.").OverridePropertyName("row");
        RuleFor(x => x.Number).GreaterThan(0).OverridePropertyName("number");
        RuleFor(x => x.Type).Must(x => Enum.IsDefined((SeatType)x)).WithMessage("Unknown seat type.").OverridePropertyName("type");
    }
}
