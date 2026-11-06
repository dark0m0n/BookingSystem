using FluentValidation;
using TicketBooking.Contracts.Administration;
using TicketBooking.Domain.Catalogue;

namespace TicketBooking.Api.Validation;

public sealed class CreateEventRequestValidator : AbstractValidator<CreateEventRequest>
{
    public CreateEventRequestValidator()
    {
        RuleFor(x => x.Title).Must(x => RequestRules.Text(x, 200)).WithMessage("A title of at most 200 characters is required.").OverridePropertyName("title");
        RuleFor(x => x.Description).NotNull().MaximumLength(10000).OverridePropertyName("description");
        RuleFor(x => x.DurationMinutes).GreaterThan(0).OverridePropertyName("durationMinutes");
        RuleFor(x => x.AgeRating).GreaterThanOrEqualTo(0).OverridePropertyName("ageRating");
        RuleFor(x => x.Status).Must(x => Enum.IsDefined((EventStatus)x)).WithMessage("Unknown event status.").OverridePropertyName("status");
    }
}
