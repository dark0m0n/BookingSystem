using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TicketBooking.Api.Errors;

public sealed class ProblemDetailsOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var path = context.Description.RelativePath ?? "";
        if (!(path.StartsWith("api/admin/", StringComparison.Ordinal) || path.StartsWith("api/events", StringComparison.Ordinal) ||
            path.StartsWith("api/sessions", StringComparison.Ordinal) || path.StartsWith("api/holds/", StringComparison.Ordinal) ||
            path.StartsWith("api/bookings", StringComparison.Ordinal) || path == "api/me/bookings") || operation.Responses is null) return Task.CompletedTask;
        if (path is "api/me/bookings" or "api/admin/bookings")
        {
            operation.Parameters ??= [];
            foreach (var (name, description) in new[]
            {
                ("page", "One-based page number; defaults to 1. Page offset must fit Int32."),
                ("pageSize", "Page size from 1 to 100; defaults to 20.")
            })
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = name,
                    In = ParameterLocation.Query,
                    Description = description,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" }
                });
        }
        foreach (var (key, value) in operation.Responses)
        {
            if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var status) || status < 400 ||
                value is not OpenApiResponse response || response.Content is null ||
                !response.Content.TryGetValue("application/problem+json", out var mediaType)) continue;
            var error = ApiErrorCatalog.ForStatus(status);
            response.Description = $"{error.Title}. ProblemDetails includes type, title, status, instance, code and traceId." +
                (status == 400 ? " Validation failures include camelCase field errors; malformed JSON/binding failures may omit errors." : "");
            if (status == 409 && path.EndsWith("/holds", StringComparison.Ordinal))
                response.Description += " Hold creation uses seat_not_available for unavailable/stale inventory, hold_expired if allocation outlives the deadline, and conflict for ineligible sessions or prices.";
            if (path.StartsWith("api/holds/", StringComparison.Ordinal))
            {
                if (status == 403) response.Description += " A different owner, including an Admin, receives resource_not_owned.";
                if (status == 409) response.Description += " Cancellation uses hold_expired, hold_not_active, or seat_not_available; all releases and the status transition roll back together.";
            }
            if (path == "api/bookings")
            {
                if (status == 403) response.Description += " A different hold owner, including an Admin, receives resource_not_owned.";
                if (status == 409) response.Description += " Conversion uses hold_expired, hold_not_active, seat_not_available or payment_deadline_passed; the booking, hold transition and transfers roll back together.";
            }
            if (path.StartsWith("api/bookings/", StringComparison.Ordinal) && status == 403)
                response.Description += " A different booking owner, including an Admin, receives resource_not_owned.";
            var example = new JsonObject
            {
                ["type"] = $"urn:ticketbooking:error:{error.Code}",
                ["title"] = error.Title,
                ["status"] = status,
                ["instance"] = "/" + path,
                ["code"] = error.Code,
                ["traceId"] = "example-trace-id"
            };
            if (status == 400) example["errors"] = new JsonObject { ["field"] = new JsonArray("Invalid value.") };
            else example["detail"] = error.Detail;
            mediaType.Example = example;
        }
        return Task.CompletedTask;
    }
}
