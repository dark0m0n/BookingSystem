using FluentValidation;
using TicketBooking.Contracts.Administration;
using TicketBooking.Domain.Catalogue;

namespace TicketBooking.Api.Validation;

public sealed class UpdateEventRequestValidator : AbstractValidator<UpdateEventRequest>
{
    public UpdateEventRequestValidator()
    {
        RuleFor(x => x).Must(x => x is not { Title: null, Description: null, DurationMinutes: null, AgeRating: null, Status: null })
            .WithMessage("Supply at least one event field to update.").OverridePropertyName("event");
        RuleFor(x => x.Title).Must(x => RequestRules.Text(x, 200)).When(x => x.Title is not null).OverridePropertyName("title");
        RuleFor(x => x.Description).MaximumLength(10000).OverridePropertyName("description");
        RuleFor(x => x.DurationMinutes).GreaterThan(0).When(x => x.DurationMinutes is not null).OverridePropertyName("durationMinutes");
        RuleFor(x => x.AgeRating).GreaterThanOrEqualTo(0).When(x => x.AgeRating is not null).OverridePropertyName("ageRating");
        RuleFor(x => x.Status).Must(x => x is null || Enum.IsDefined((EventStatus)x.Value)).WithMessage("Unknown event status.").OverridePropertyName("status");
    }
}
