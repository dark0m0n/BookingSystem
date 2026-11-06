using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Administration;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresFact]
    public async Task AdminEvent_CreateDraftThenUpdateAndPublishPersistsAllFields()
    {
        var token = await AdminToken();
        var created = await CreateAdmin<EventResponse>("/api/admin/events", token, new CreateEventRequest(" Film ", "", 90, 0));
        Assert.Equal("Film", created.Title);
        Assert.Equal(0, created.Status);
        var path = $"/api/admin/events/{created.Id}";
        using var response = await AdminRequest(HttpMethod.Patch, path, token,
            new { title = " Updated ", description = "New description", durationMinutes = 120, ageRating = 16, status = 1, id = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var expected = new EventResponse(created.Id, "Updated", "New description", 120, 16, 1);
        Assert.Equal(expected, await response.Content.ReadFromJsonAsync<EventResponse>());
        using var read = await AdminRequest(HttpMethod.Get, path, token);
        Assert.Equal(expected, await read.Content.ReadFromJsonAsync<EventResponse>());
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().Events.CountAsync());
    }

    [PostgresFact]
    public async Task AdminEvent_PartialUpdateKeepsOmittedFieldsAndCanClearDescription()
    {
        var token = await AdminToken();
        var created = await CreateAdmin<EventResponse>("/api/admin/events", token, new CreateEventRequest("Film", "Description", 90, 12, 1));
        var path = $"/api/admin/events/{created.Id}";
        using var response = await AdminRequest(HttpMethod.Patch, path, token, new UpdateEventRequest(Description: ""));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created with { Description = "" }, await response.Content.ReadFromJsonAsync<EventResponse>());
        using var missing = await AdminRequest(HttpMethod.Patch, $"/api/admin/events/{Guid.NewGuid()}", token, new UpdateEventRequest(Status: 1));
        await AssertProblem(missing, 404, "not_found");
    }

    [PostgresTheory]
    [InlineData("title")]
    [InlineData("long-title")]
    [InlineData("description")]
    [InlineData("duration")]
    [InlineData("negative-duration")]
    [InlineData("age")]
    [InlineData("status")]
    public async Task AdminEvent_InvalidCreationAndUpdateUseFieldErrorsWithoutWrites(string defect)
    {
        var token = await AdminToken();
        var created = await CreateAdmin<EventResponse>("/api/admin/events", token, new CreateEventRequest("Film", "Description", 90, 12, 1));
        var input = new CreateEventRequest("Changed", "Changed", 120, 16, 2);
        var (invalid, field) = defect switch
        {
            "title" => (input with { Title = " " }, "title"),
            "long-title" => (input with { Title = new string('X', 201) }, "title"),
            "description" => (input with { Description = new string('X', 10001) }, "description"),
            "duration" => (input with { DurationMinutes = 0 }, "durationMinutes"),
            "negative-duration" => (input with { DurationMinutes = -1 }, "durationMinutes"),
            "age" => (input with { AgeRating = -1 }, "ageRating"),
            "status" => (input with { Status = 999 }, "status"),
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Patch })
        {
            var path = method == HttpMethod.Post ? "/api/admin/events" : $"/api/admin/events/{created.Id}";
            using var response = await AdminRequest(method, path, token, invalid);
            var problem = await AssertProblem(response, 400, "validation_failed");
            Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
        }
        using var read = await AdminRequest(HttpMethod.Get, $"/api/admin/events/{created.Id}", token);
        Assert.Equal(created, await read.Content.ReadFromJsonAsync<EventResponse>());
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().Events.CountAsync());
    }

    [PostgresFact]
    public async Task AdminEvent_EmptyOrUnknownFieldUpdateIsRejectedAndNullFieldsKeepStoredValues()
    {
        var token = await AdminToken();
        var created = await CreateAdmin<EventResponse>("/api/admin/events", token, new CreateEventRequest("Film", "Description", 90, 12, 1));
        var path = $"/api/admin/events/{created.Id}";
        foreach (var body in new object[] { new { }, new { id = Guid.NewGuid() }, new UpdateEventRequest() })
        {
            using var response = await AdminRequest(HttpMethod.Patch, path, token, body);
            await AssertProblem(response, 400, "validation_failed");
        }
        using var updated = await AdminRequest(HttpMethod.Patch, path, token, new { title = (string?)null, status = 2 });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(created with { Status = 2 }, await updated.Content.ReadFromJsonAsync<EventResponse>());
    }

    [PostgresFact]
    public async Task AdminEvent_UpdatesAffectNewSessionsAndKeepExistingSessionTimesAndInventory()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var existing = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        var path = $"/api/admin/events/{setup.Event.Id}";
        using var edited = await AdminRequest(HttpMethod.Patch, path, token, new UpdateEventRequest(DurationMinutes: 120, Status: 2));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        using var blocked = await AdminRequest(HttpMethod.Post, "/api/admin/sessions", token, SessionRequest(setup));
        await AssertProblem(blocked, 409, "conflict");
        using var published = await AdminRequest(HttpMethod.Patch, path, token, new UpdateEventRequest(Status: 1));
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var next = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        Assert.Equal(next.StartsAt.AddMinutes(120), next.EndsAt);
        using var read = await AdminRequest(HttpMethod.Get, $"/api/admin/sessions/{existing.Id}", token);
        var stored = (await read.Content.ReadFromJsonAsync<SessionResponse>())!;
        Assert.Equal(existing.EndsAt, stored.EndsAt);
        Assert.Equal(existing.Seats, stored.Seats);
    }

    [PostgresFact]
    public async Task AdminEvent_ConcurrentPartialUpdatesPreserveBothChanges()
    {
        var token = await AdminToken();
        var created = await CreateAdmin<EventResponse>("/api/admin/events", token, new CreateEventRequest("Film", "Description", 90, 12, 1));
        var path = $"/api/admin/events/{created.Id}";
        var responses = await Task.WhenAll(
            AdminRequest(HttpMethod.Patch, path, token, new UpdateEventRequest(Title: "Updated")),
            AdminRequest(HttpMethod.Patch, path, token, new UpdateEventRequest(DurationMinutes: 120)));
        try
        {
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            using var read = await AdminRequest(HttpMethod.Get, path, token);
            Assert.Equal(created with { Title = "Updated", DurationMinutes = 120 }, await read.Content.ReadFromJsonAsync<EventResponse>());
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [PostgresFact]
    public async Task AdminEvent_ArchivingRacingSchedulingEitherCapturesPublishedEventOrRejectsSession()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var responses = await Task.WhenAll(
            AdminRequest(HttpMethod.Patch, $"/api/admin/events/{setup.Event.Id}", token, new UpdateEventRequest(DurationMinutes: 120, Status: 2)),
            AdminRequest(HttpMethod.Post, "/api/admin/sessions", token, SessionRequest(setup)));
        using var edit = responses[0];
        using var scheduled = responses[1];
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        if (scheduled.StatusCode == HttpStatusCode.Created)
        {
            var session = (await scheduled.Content.ReadFromJsonAsync<SessionResponse>())!;
            Assert.Equal(session.StartsAt.AddMinutes(90), session.EndsAt);
            Assert.Equal(setup.Seats.Length, session.Seats.Count);
        }
        else
        {
            await AssertProblem(scheduled, 409, "conflict");
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            Assert.Empty(await db.Sessions.ToListAsync());
            Assert.Empty(await db.SessionSeats.ToListAsync());
        }
    }
}
