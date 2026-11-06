using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TicketBooking.Contracts.Administration;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresFact]
    public async Task FluentValidation_ProducesIndexedStableProblemsAfterAuthorization()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var path = $"/api/admin/auditoriums/{setup.Hall.Id}/seats";
        var body = new AddSeatsRequest([null, new(" ", 0, 999)]);
        using var anonymous = await AdminRequest(HttpMethod.Post, path, null, body);
        await AssertProblem(anonymous, 401, "unauthorized");
        // Obtain a Customer-only token via an independent registration.
        using var registration = await Register("customer@example.test");
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var login = await Login("customer@example.test", Password);
        var customer = (await login.Content.ReadFromJsonAsync<TicketBooking.Contracts.Auth.LoginResponse>())!.AccessToken;
        using var denied = await AdminRequest(HttpMethod.Post, path, customer, body);
        await AssertProblem(denied, 403, "forbidden");
        using var invalid = await AdminRequest(HttpMethod.Post, path, token, body);
        var problem = await AssertProblem(invalid, 400, "validation_failed");
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("seats[0]", out _));
        Assert.True(errors.TryGetProperty("seats[1].row", out _));
        Assert.True(errors.TryGetProperty("seats[1].number", out _));
        Assert.True(errors.TryGetProperty("seats[1].type", out _));
    }

    [PostgresFact]
    public async Task MalformedJsonAndUnsupportedContentTypeUseDocumentedProblemContract()
    {
        var token = await AdminToken();
        foreach (var body in new[] { "{", "null", "{\"durationMinutes\":\"bad\"}" })
        {
            using var malformed = new HttpRequestMessage(HttpMethod.Post, "/api/admin/events");
            malformed.Headers.Authorization = new("Bearer", token);
            malformed.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var badJson = await client.SendAsync(malformed);
            await AssertProblem(badJson, 400, "validation_failed");
        }
        using var unsupported = new HttpRequestMessage(HttpMethod.Post, "/api/admin/events");
        unsupported.Headers.Authorization = new("Bearer", token);
        unsupported.Content = new StringContent("text", Encoding.UTF8, "text/plain");
        using var media = await client.SendAsync(unsupported);
        await AssertProblem(media, 415, "http_error");
    }

    [PostgresFact]
    public async Task GeneratedOpenApi_DocumentsProblemSchemasStatusCodesAndSafeExamples()
    {
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        foreach (var (path, method, statuses) in new[]
        {
            ("/api/events", "get", new[] { 400, 500 }),
            ("/api/events/{id}/sessions", "get", new[] { 400, 404, 500 }),
            ("/api/sessions/{id}/seats", "get", new[] { 404, 409, 500 }),
            ("/api/admin/sessions", "post", new[] { 400, 401, 403, 404, 409, 415, 500 }),
            ("/api/admin/auditoriums/{id}/seats", "post", new[] { 400, 401, 403, 404, 409, 415, 500 })
        })
        {
            var responses = paths.GetProperty(path).GetProperty(method).GetProperty("responses");
            foreach (var status in statuses)
            {
                var problem = responses.GetProperty(status.ToString());
                Assert.Contains("ProblemDetails", problem.GetProperty("description").GetString());
                var content = problem.GetProperty("content").GetProperty("application/problem+json");
                Assert.True(content.TryGetProperty("schema", out _));
                var example = content.GetProperty("example");
                Assert.Equal(status, example.GetProperty("status").GetInt32());
                Assert.True(example.TryGetProperty("code", out _));
                Assert.True(example.TryGetProperty("traceId", out _));
                if (status == 400) Assert.True(example.TryGetProperty("errors", out _));
            }
        }
    }
}
