using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace TicketBooking.Api.Errors;

public static class ApiProblemDetails
{
    public static IServiceCollection AddApiErrors(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context => Enrich(context.HttpContext, context.ProblemDetails));
        services.AddExceptionHandler<ApiExceptionHandler>();
        return services;
    }

    public static void Enrich(HttpContext context, ProblemDetails problem)
    {
        problem.Instance ??= context.Request.Path;
        problem.Extensions.TryAdd("traceId", Activity.Current?.Id ?? context.TraceIdentifier);
        problem.Extensions.TryAdd("code", ApiErrorCatalog.ForStatus(problem.Status ?? 500).Code);
    }

    public static Task WriteStatusCodeAsync(StatusCodeContext context) =>
        WriteAsync(context.HttpContext, ApiErrorCatalog.ForStatus(context.HttpContext.Response.StatusCode),
            context.HttpContext.RequestAborted);

    public static async Task WriteAsync(HttpContext context, ApiErrorDefinition error, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = error.Status;
        var problem = new ProblemDetails
        {
            Type = $"urn:ticketbooking:error:{error.Code}",
            Title = error.Title,
            Status = error.Status,
            Detail = error.Detail
        };
        problem.Extensions["code"] = error.Code;
        Enrich(context, problem);
        var service = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        if (!await service.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem }))
        {
            // This is an API: preserve the error contract even for unsupported Accept headers.
            await context.Response.WriteAsJsonAsync(problem, options: null,
                contentType: "application/problem+json", cancellationToken: cancellationToken);
        }
    }
}
