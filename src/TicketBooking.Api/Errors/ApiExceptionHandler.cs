using Microsoft.AspNetCore.Diagnostics;

namespace TicketBooking.Api.Errors;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var error = ApiErrorCatalog.ForException(exception);
        if (error.Status >= 500)
        {
            logger.LogError(exception, "Unhandled request error. TraceId: {TraceId}", httpContext.TraceIdentifier);
        }

        await ApiProblemDetails.WriteAsync(httpContext, error, cancellationToken);
        return true;
    }
}
