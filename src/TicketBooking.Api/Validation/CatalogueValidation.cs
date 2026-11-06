using FluentValidation;
using FluentValidation.Results;
using TicketBooking.Api.Errors;
using TicketBooking.Contracts.Administration;

namespace TicketBooking.Api.Validation;

public static class CatalogueValidation
{
    public static IServiceCollection AddCatalogueValidation(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<CreateVenueRequest>, CreateVenueRequestValidator>();
        services.AddSingleton<IValidator<CreateAuditoriumRequest>, CreateAuditoriumRequestValidator>();
        services.AddSingleton<IValidator<UpdateAuditoriumRequest>, UpdateAuditoriumRequestValidator>();
        services.AddSingleton<SeatRequestValidator>();
        services.AddSingleton<IValidator<SeatRequest>>(s => s.GetRequiredService<SeatRequestValidator>());
        services.AddSingleton<IValidator<AddSeatsRequest>, AddSeatsRequestValidator>();
        services.AddSingleton<IValidator<CreateEventRequest>, CreateEventRequestValidator>();
        services.AddSingleton<IValidator<UpdateEventRequest>, UpdateEventRequestValidator>();
        services.AddSingleton<IValidator<CreateSessionRequest>, CreateSessionRequestValidator>();
        services.AddSingleton<IValidator<UpdateSessionRequest>, UpdateSessionRequestValidator>();
        services.AddSingleton<IValidator<CatalogueQuery>, CatalogueQueryValidator>();
        return services;
    }

    public static RouteHandlerBuilder ValidateRequest<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var request = context.Arguments.OfType<T>().SingleOrDefault();
            if (request is null)
                return ApiProblemDetails.Validation(context.HttpContext,
                    new ValidationResult([new ValidationFailure("request", "A request body is required.")]));
            var validator = context.HttpContext.RequestServices.GetRequiredService<IValidator<T>>();
            var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);
            return result.IsValid ? await next(context) : ApiProblemDetails.Validation(context.HttpContext, result);
        }).ProducesValidationProblem().ProducesProblem(415);
}
