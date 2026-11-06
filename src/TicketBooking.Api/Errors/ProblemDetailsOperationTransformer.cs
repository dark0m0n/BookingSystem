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
            path.StartsWith("api/sessions", StringComparison.Ordinal)) || operation.Responses is null) return Task.CompletedTask;
        foreach (var (key, value) in operation.Responses)
        {
            if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var status) || status < 400 ||
                value is not OpenApiResponse response || response.Content is null ||
                !response.Content.TryGetValue("application/problem+json", out var mediaType)) continue;
            var error = ApiErrorCatalog.ForStatus(status);
            response.Description = $"{error.Title}. ProblemDetails includes type, title, status, instance, code and traceId." +
                (status == 400 ? " Validation failures include camelCase field errors; malformed JSON/binding failures may omit errors." : "");
            var example = new JsonObject
            {
                ["type"] = $"urn:ticketbooking:error:{error.Code}", ["title"] = error.Title, ["status"] = status,
                ["instance"] = "/" + path, ["code"] = error.Code, ["traceId"] = "example-trace-id"
            };
            if (status == 400) example["errors"] = new JsonObject { ["field"] = new JsonArray("Invalid value.") };
            else example["detail"] = error.Detail;
            mediaType.Example = example;
        }
        return Task.CompletedTask;
    }
}
