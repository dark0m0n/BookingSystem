using FluentValidation;
using TicketBooking.Contracts.Reservation;

namespace TicketBooking.Api.Validation;

public static class HoldValidation
{
    public static IServiceCollection AddHoldValidation(this IServiceCollection services) =>
        services.AddSingleton<IValidator<CreateHoldRequest>, CreateHoldRequestValidator>();
}

public sealed class CreateHoldRequestValidator : AbstractValidator<CreateHoldRequest>
{
    public CreateHoldRequestValidator()
    {
        RuleFor(request => request.SessionSeatIds).Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("A seat selection is required.")
            .NotEmpty().WithMessage("Select at least one seat.")
            .Must(ids => ids!.Count <= 100)
            .WithMessage("Select at most 100 seats.")
            .Must(ids => ids!.All(id => id != Guid.Empty)).WithMessage("Seat identifiers must be nonempty.")
            .Must(ids => ids!.Distinct().Count() == ids!.Count).WithMessage("Seat identifiers must be unique.")
            .OverridePropertyName("sessionSeatIds");
    }
}
