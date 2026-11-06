using FluentValidation;
using TicketBooking.Contracts.Administration;

namespace TicketBooking.Api.Validation;

public sealed class AddSeatsRequestValidator : AbstractValidator<AddSeatsRequest>
{
    public AddSeatsRequestValidator(SeatRequestValidator seats)
    {
        RuleFor(x => x.Seats).Cascade(CascadeMode.Stop)
            .Must(x => x is { Count: > 0 and <= 1000 }).WithMessage("Supply between 1 and 1000 seats.").OverridePropertyName("seats")
            .Custom((items, context) =>
            {
                for (var index = 0; index < items!.Count; index++)
                {
                    if (items[index] is not { } seat) { context.AddFailure($"seats[{index}]", "A seat is required."); continue; }
                    foreach (var error in seats.Validate(seat).Errors)
                        context.AddFailure($"seats[{index}].{error.PropertyName}", error.ErrorMessage);
                }
            });
    }
}
