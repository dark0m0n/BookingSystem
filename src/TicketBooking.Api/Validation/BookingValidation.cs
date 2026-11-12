using FluentValidation;
using TicketBooking.Contracts.Booking;

namespace TicketBooking.Api.Validation;

public static class BookingValidation
{
    public static IServiceCollection AddBookingValidation(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<CreateBookingRequest>, CreateBookingRequestValidator>();
        services.AddSingleton<IValidator<BookingListQuery>, BookingListQueryValidator>();
        return services;
    }
}

public sealed class CreateBookingRequestValidator : AbstractValidator<CreateBookingRequest>
{
    public CreateBookingRequestValidator()
    {
        RuleFor(request => request.HoldId).NotEmpty().WithMessage("A nonempty hold identifier is required.")
            .OverridePropertyName("holdId");
    }
}
