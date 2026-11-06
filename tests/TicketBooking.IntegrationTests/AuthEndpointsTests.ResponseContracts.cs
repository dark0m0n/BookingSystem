using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using TicketBooking.Contracts.Administration;
using TicketBooking.Contracts.Catalogue;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresFact]
    public async Task PublicResponses_ExposeOnlyDocumentedDtoFieldsAndDeclareConcreteSuccessTypes()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var session = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        var envelope = new[] { "items", "page", "pageSize", "totalCount", "totalPages", "hasPreviousPage", "hasNextPage" };
        var eventFields = new[] { "id", "title", "description", "durationMinutes", "ageRating" };
        var sessionFields = new[] { "id", "eventId", "eventTitle", "auditoriumId", "auditoriumName", "venueId", "venueName", "venueAddress", "startsAt", "endsAt", "status" };
        var cases = new (string Path, string Pattern, Type ResponseType, string[] Fields, string? Children, string[]? ChildFields)[]
        {
            ("/api/events", "/api/events", typeof(EventListResponse), envelope, "items", eventFields),
            ($"/api/events/{setup.Event.Id}", "/api/events/{id:guid}", typeof(EventSummaryResponse), eventFields, null, null),
            ($"/api/events/{setup.Event.Id}/sessions", "/api/events/{id:guid}/sessions", typeof(EventSessionListResponse), envelope, "items", sessionFields),
            ($"/api/sessions/{session.Id}", "/api/sessions/{id:guid}", typeof(SessionDetailsResponse), sessionFields, null, null),
            ($"/api/sessions/{session.Id}/seats", "/api/sessions/{id:guid}/seats", typeof(SessionSeatMapResponse),
                ["sessionId", "auditoriumId", "seats"], "seats", ["id", "seatId", "row", "number", "type", "price", "currency", "status", "version"])
        };
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().ToArray();
        foreach (var (path, pattern, responseType, fields, children, childFields) in cases)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            AssertFields(json.RootElement, fields);
            if (children is not null)
                foreach (var child in json.RootElement.GetProperty(children).EnumerateArray()) AssertFields(child, childFields!);
            var endpoint = Assert.Single(endpoints, e => e.RoutePattern.RawText == pattern);
            var success = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Where(m => m.StatusCode == 200).ToArray();
            Assert.NotEmpty(success);
            Assert.All(success, m => Assert.Equal(responseType, m.Type));
            Assert.Equal(typeof(EventSummaryResponse).Assembly, responseType.Assembly);
        }
    }

    [PostgresFact]
    public async Task PublicPagination_ReturnsNavigationForFirstLastEmptyAndBeyondEndPages()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        await CreateAdmin<EventResponse>("/api/admin/events", token, new CreateEventRequest("Other", "", 90, 0, 1));
        foreach (var path in new[] { "/api/events", $"/api/events/{setup.Event.Id}/sessions" })
        {
            foreach (var page in new[] { 1, 2, 3 })
            {
                using var response = await client.GetAsync($"{path}?page={page}&pageSize=1");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = document.RootElement;
                Assert.Equal(page, root.GetProperty("page").GetInt32());
                Assert.Equal(1, root.GetProperty("pageSize").GetInt32());
                Assert.Equal(2, root.GetProperty("totalCount").GetInt32());
                Assert.Equal(2, root.GetProperty("totalPages").GetInt32());
                Assert.Equal(page > 1, root.GetProperty("hasPreviousPage").GetBoolean());
                Assert.Equal(page < 2, root.GetProperty("hasNextPage").GetBoolean());
                Assert.Equal(page <= 2 ? 1 : 0, root.GetProperty("items").GetArrayLength());
            }
            using var empty = await client.GetAsync($"{path}?date=9999-12-31");
            using var emptyDocument = JsonDocument.Parse(await empty.Content.ReadAsStringAsync());
            Assert.Equal(0, emptyDocument.RootElement.GetProperty("totalPages").GetInt32());
            Assert.False(emptyDocument.RootElement.GetProperty("hasPreviousPage").GetBoolean());
            Assert.False(emptyDocument.RootElement.GetProperty("hasNextPage").GetBoolean());
        }
    }

    private static void AssertFields(JsonElement element, IEnumerable<string> expected) =>
        Assert.Equal(expected.Order(StringComparer.Ordinal), element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
}
