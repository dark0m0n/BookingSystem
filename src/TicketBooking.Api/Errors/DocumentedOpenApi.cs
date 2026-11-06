namespace TicketBooking.Api.Errors;

public static class DocumentedOpenApi
{
    public static IServiceCollection AddDocumentedOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options => options.AddOperationTransformer<ProblemDetailsOperationTransformer>());
        return services;
    }
}
