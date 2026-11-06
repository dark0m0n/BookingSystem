using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Administration;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Identity;
using TicketBooking.Infrastructure.Identity;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    private readonly AdminInventoryWriteFailure adminInventoryWriteFailure = new();

    [PostgresFact]
    public async Task Administration_AllRoutesRequireAuthenticationAndAdminRoleBeforeWrites()
    {
        using var registered = await Register();
        var customer = await LoginToken();
        var id = Guid.NewGuid();
        var routes = AdministrationRequests(id);
        foreach (var (method, path, body) in routes)
        {
            using var anonymous = await AdminRequest(method, path, null, body);
            await AssertProblem(anonymous, 401, "unauthorized");
            using var denied = await AdminRequest(method, path, customer.AccessToken, body);
            await AssertProblem(denied, 403, "forbidden");
        }
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Empty(await db.Venues.ToArrayAsync());
        Assert.Empty(await db.Events.ToArrayAsync());
        Assert.Empty(await db.Sessions.ToArrayAsync());
    }

    [PostgresFact]
    public async Task Admin_CreatesCompleteSessionWithServerGeneratedInventoryAndUtcTimes()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var otherHall = await CreateAdmin<AuditoriumResponse>("/api/admin/auditoriums", token, new CreateAuditoriumRequest(setup.Venue.Id, "Other hall"));
        await CreateAdmin<SeatResponse[]>($"/api/admin/auditoriums/{otherHall.Id}/seats", token, new AddSeatsRequest([new("X", 1)]));
        var start = clock.GetUtcNow().AddHours(2).ToOffset(TimeSpan.FromHours(2));
        var session = await CreateAdmin<SessionResponse>("/api/admin/sessions", token,
            new
            {
                eventId = setup.Event.Id,
                auditoriumId = setup.Hall.Id,
                startsAt = start,
                price = 125.50m,
                currency = "eur",
                endsAt = start.AddMinutes(1),
                status = 2,
                seats = new[] { new { id = Guid.Empty, seatId = Guid.NewGuid(), price = 0m, currency = "USD", status = 2, version = 99 } }
            });

        Assert.NotEqual(Guid.Empty, session.Id);
        Assert.Equal(setup.Event.Id, session.EventId);
        Assert.Equal(setup.Hall.Id, session.AuditoriumId);
        Assert.Equal(TimeSpan.Zero, session.StartsAt.Offset);
        Assert.Equal(session.StartsAt.AddMinutes(90), session.EndsAt);
        Assert.Equal(0, session.Status);
        Assert.Equal(setup.Seats.Select(s => s.Id).Order(), session.Seats.Select(s => s.SeatId).Order());
        Assert.All(session.Seats, seat =>
        {
            Assert.NotEqual(Guid.Empty, seat.Id);
            Assert.Equal(125.50m, seat.Price);
            Assert.Equal("EUR", seat.Currency);
            Assert.Equal(0, seat.Status);
            Assert.Equal(0, seat.Version);
        });
        using var read = await AdminRequest(HttpMethod.Get, $"/api/admin/sessions/{session.Id}", token);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var stored = (await read.Content.ReadFromJsonAsync<SessionResponse>())!;
        Assert.Equal(session.StartsAt, stored.StartsAt);
        Assert.Equal(session.Seats, stored.Seats);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Equal(1, await db.Sessions.CountAsync());
        Assert.Equal(setup.Seats.Length, await db.SessionSeats.CountAsync());
    }

    [PostgresFact]
    public async Task AdminInvalidFieldsAndMissingParents_UseStableErrorsWithoutPartialWrites()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var requests = new (string Path, object Body, int Status)[]
        {
            ("/api/admin/venues", new CreateVenueRequest(" ", "Address"), 400),
            ("/api/admin/venues", new CreateVenueRequest("Venue", null), 400),
            ("/api/admin/auditoriums", new CreateAuditoriumRequest(Guid.Empty, "Hall"), 400),
            ("/api/admin/auditoriums", new CreateAuditoriumRequest(setup.Venue.Id, " "), 400),
            ("/api/admin/auditoriums", new CreateAuditoriumRequest(Guid.NewGuid(), "Hall"), 404),
            ("/api/admin/events", new CreateEventRequest(" ", "", 90, 0), 400),
            ("/api/admin/events", new CreateEventRequest("Film", null, 90, 0), 400),
            ("/api/admin/events", new CreateEventRequest("Film", "", 0, -1, 999), 400)
        };
        foreach (var request in requests)
        {
            using var response = await AdminRequest(HttpMethod.Post, request.Path, token, request.Body);
            await AssertProblem(response, request.Status, request.Status == 400 ? "validation_failed" : "not_found");
        }
        using var rename = await AdminRequest(HttpMethod.Patch, $"/api/admin/auditoriums/{setup.Hall.Id}", token, new UpdateAuditoriumRequest(" "));
        await AssertProblem(rename, 400, "validation_failed");
        using var missing = await AdminRequest(HttpMethod.Post, $"/api/admin/auditoriums/{Guid.NewGuid()}/seats", token,
            new AddSeatsRequest([new("B", 1)]));
        await AssertProblem(missing, 404, "not_found");
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Single(await db.Venues.ToArrayAsync());
        Assert.Equal("Hall", (await db.Auditoriums.SingleAsync()).Name);
        Assert.Single(await db.Events.ToArrayAsync());
        Assert.Equal(setup.Seats.Length, await db.Seats.CountAsync());
    }

    [PostgresFact]
    public async Task AuditoriumManagement_CanReadRenameAndRemoveUnusedLayouts()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        using var renamed = await AdminRequest(HttpMethod.Patch, $"/api/admin/auditoriums/{setup.Hall.Id}", token,
            new UpdateAuditoriumRequest(" Renamed hall "));
        Assert.Equal("Renamed hall", (await renamed.Content.ReadFromJsonAsync<AuditoriumResponse>())!.Name);
        foreach (var path in new[] { "/api/admin/venues", $"/api/admin/venues/{setup.Venue.Id}",
            $"/api/admin/auditoriums/{setup.Hall.Id}", $"/api/admin/venues/{setup.Venue.Id}/auditoriums",
            $"/api/admin/auditoriums/{setup.Hall.Id}/seats", $"/api/admin/events/{setup.Event.Id}" })
        {
            using var read = await AdminRequest(HttpMethod.Get, path, token);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        }
        using var deletedSeat = await AdminRequest(HttpMethod.Delete, $"/api/admin/auditoriums/{setup.Hall.Id}/seats/{setup.Seats[0].Id}", token);
        Assert.Equal(HttpStatusCode.NoContent, deletedSeat.StatusCode);
        using var deleted = await AdminRequest(HttpMethod.Delete, $"/api/admin/auditoriums/{setup.Hall.Id}", token);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Empty(await db.Auditoriums.ToArrayAsync());
        Assert.Empty(await db.Seats.ToArrayAsync());
        Assert.Single(await db.Venues.ToArrayAsync());
    }

    [PostgresFact]
    public async Task AuditoriumWithSessions_RejectsLayoutChangesAndDeletionButAllowsRename()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var session = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        using var add = await AdminRequest(HttpMethod.Post, $"/api/admin/auditoriums/{setup.Hall.Id}/seats", token, new AddSeatsRequest([new("B", 1)]));
        await AssertProblem(add, 409, "conflict");
        using var remove = await AdminRequest(HttpMethod.Delete, $"/api/admin/auditoriums/{setup.Hall.Id}/seats/{setup.Seats[0].Id}", token);
        await AssertProblem(remove, 409, "conflict");
        using var update = await AdminRequest(HttpMethod.Patch, $"/api/admin/auditoriums/{setup.Hall.Id}/seats/{setup.Seats[0].Id}", token, new SeatRequest("B", 1));
        await AssertProblem(update, 409, "conflict");
        using var delete = await AdminRequest(HttpMethod.Delete, $"/api/admin/auditoriums/{setup.Hall.Id}", token);
        await AssertProblem(delete, 409, "conflict");
        using var rename = await AdminRequest(HttpMethod.Patch, $"/api/admin/auditoriums/{setup.Hall.Id}", token, new UpdateAuditoriumRequest("New label"));
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Equal(setup.Seats.Length, await db.Seats.CountAsync());
        Assert.Equal(session.Seats.Count, await db.SessionSeats.CountAsync());
        Assert.All(await db.SessionSeats.ToListAsync(), seat => Assert.Equal(0, seat.Version));
    }

    [PostgresTheory]
    [InlineData("duplicate", 409)]
    [InlineData("existing", 409)]
    [InlineData("empty", 400)]
    [InlineData("null-seat", 400)]
    [InlineData("row", 400)]
    [InlineData("number", 400)]
    [InlineData("type", 400)]
    public async Task SeatBatches_InvalidInputPersistsNoPartialLayout(string defect, int status)
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        SeatRequest?[] input = defect switch
        {
            "duplicate" => [new("B", 1), new(" B ", 1)],
            "existing" => [new("B", 1), new("A", 1)],
            "empty" => [],
            "null-seat" => [new("B", 1), null],
            "row" => [new(" ", 1)],
            "number" => [new("B", 0)],
            "type" => [new("B", 1, 999)],
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        using var response = await AdminRequest(HttpMethod.Post, $"/api/admin/auditoriums/{setup.Hall.Id}/seats", token, new AddSeatsRequest(input));
        await AssertProblem(response, status, status == 400 ? "validation_failed" : "conflict");
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Equal(setup.Seats.Length, await scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().Seats.CountAsync());
    }

    [PostgresTheory]
    [InlineData("price", 400)]
    [InlineData("negative-price", 400)]
    [InlineData("missing-price", 400)]
    [InlineData("price-overflow", 400)]
    [InlineData("missing-currency", 400)]
    [InlineData("blank-currency", 400)]
    [InlineData("currency", 400)]
    [InlineData("now", 400)]
    [InlineData("past", 400)]
    [InlineData("missing-event", 404)]
    [InlineData("missing-hall", 404)]
    [InlineData("draft", 409)]
    [InlineData("empty-layout", 409)]
    public async Task InvalidSession_DoesNotPersistASessionOrInventory(string defect, int status)
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var request = SessionRequest(setup);
        request = defect switch
        {
            "price" => request with { Price = 10.001m },
            "negative-price" => request with { Price = -1m },
            "missing-price" => request with { Price = null },
            "price-overflow" => request with { Price = TicketBooking.Domain.Money.MaxAmount + 0.01m },
            "missing-currency" => request with { Currency = null },
            "blank-currency" => request with { Currency = " " },
            "currency" => request with { Currency = "XYZ" },
            "now" => request with { StartsAt = clock.GetUtcNow() },
            "past" => request with { StartsAt = clock.GetUtcNow().AddTicks(-1) },
            "missing-event" => request with { EventId = Guid.NewGuid() },
            "missing-hall" => request with { AuditoriumId = Guid.NewGuid() },
            "draft" => request with
            {
                EventId = (await CreateAdmin<EventResponse>("/api/admin/events", token,
                new CreateEventRequest("Draft", "", 90, 0))).Id
            },
            "empty-layout" => request with
            {
                AuditoriumId = (await CreateAdmin<AuditoriumResponse>("/api/admin/auditoriums", token,
                new CreateAuditoriumRequest(setup.Venue.Id, "Empty"))).Id
            },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        using var response = await AdminRequest(HttpMethod.Post, "/api/admin/sessions", token, request);
        await AssertProblem(response, status, status switch { 400 => "validation_failed", 404 => "not_found", _ => "conflict" });
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Empty(await db.Sessions.ToArrayAsync());
        Assert.Empty(await db.SessionSeats.ToArrayAsync());
    }

    [PostgresFact]
    public async Task SessionInventoryFailure_RollsBackSessionAndPermitsRetry()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        adminInventoryWriteFailure.FailNext();
        using var failed = await AdminRequest(HttpMethod.Post, "/api/admin/sessions", token, SessionRequest(setup));
        await AssertProblem(failed, 500, "internal_error");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            Assert.Empty(await db.Sessions.ToArrayAsync());
            Assert.Empty(await db.SessionSeats.ToArrayAsync());
        }
        var session = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        Assert.Equal(setup.Seats.Length, session.Seats.Count);
    }

    [PostgresFact]
    public async Task ConcurrentLayoutEditAndSessionCreation_AlwaysProduceACompleteSeatMap()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var operations = await Task.WhenAll(
            AdminRequest(HttpMethod.Post, $"/api/admin/auditoriums/{setup.Hall.Id}/seats", token, new AddSeatsRequest([new("B", 1)])),
            AdminRequest(HttpMethod.Post, "/api/admin/sessions", token, SessionRequest(setup)));
        using var edit = operations[0];
        using var created = operations[1];
        Assert.Contains(edit.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Equal(await db.Seats.CountAsync(), await db.SessionSeats.CountAsync());
        Assert.Equal(1, await db.Sessions.CountAsync());
    }

    [PostgresFact]
    public async Task ConcurrentIdenticalSeatBatches_CreateOneCoordinateAndReturnSafeConflict()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var path = $"/api/admin/auditoriums/{setup.Hall.Id}/seats";
        var input = new AddSeatsRequest([new("B", 1)]);
        var operations = await Task.WhenAll(AdminRequest(HttpMethod.Post, path, token, input), AdminRequest(HttpMethod.Post, path, token, input));
        try
        {
            Assert.Single(operations, r => r.StatusCode == HttpStatusCode.Created);
            await AssertProblem(Assert.Single(operations, r => r.StatusCode == HttpStatusCode.Conflict), 409, "conflict");
            await using var scope = app.Services.CreateAsyncScope();
            Assert.Equal(setup.Seats.Length + 1, await scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().Seats.CountAsync());
        }
        finally { foreach (var response in operations) response.Dispose(); }
    }

    private (HttpMethod Method, string Path, object? Body)[] AdministrationRequests(Guid id) =>
        [
            (HttpMethod.Post, "/api/admin/venues", new CreateVenueRequest("Venue", "Address")),
            (HttpMethod.Get, "/api/admin/venues", null),
            (HttpMethod.Get, $"/api/admin/venues/{id}", null),
            (HttpMethod.Post, "/api/admin/auditoriums", new CreateAuditoriumRequest(id, "Hall")),
            (HttpMethod.Get, $"/api/admin/venues/{id}/auditoriums", null),
            (HttpMethod.Get, $"/api/admin/auditoriums/{id}", null),
            (HttpMethod.Patch, $"/api/admin/auditoriums/{id}", new UpdateAuditoriumRequest("New name")),
            (HttpMethod.Delete, $"/api/admin/auditoriums/{id}", null),
            (HttpMethod.Post, $"/api/admin/auditoriums/{id}/seats", new AddSeatsRequest([new("A", 1)])),
            (HttpMethod.Get, $"/api/admin/auditoriums/{id}/seats", null),
            (HttpMethod.Patch, $"/api/admin/auditoriums/{id}/seats/{id}", new SeatRequest("B", 1)),
            (HttpMethod.Delete, $"/api/admin/auditoriums/{id}/seats/{id}", null),
            (HttpMethod.Post, "/api/admin/events", new CreateEventRequest("Film", "Description", 90, 12, 1)),
            (HttpMethod.Get, $"/api/admin/events/{id}", null),
            (HttpMethod.Patch, $"/api/admin/events/{id}", new UpdateEventRequest(Title: "Updated film")),
            (HttpMethod.Post, "/api/admin/sessions", new CreateSessionRequest(id, id, clock.GetUtcNow().AddHours(2), 100m, "UAH")),
            (HttpMethod.Get, $"/api/admin/sessions/{id}", null),
            (HttpMethod.Patch, $"/api/admin/sessions/{id}", new UpdateSessionRequest(StartsAt: clock.GetUtcNow().AddHours(3)))
        ];

    private async Task<string> AdminToken()
    {
        using var registered = await Register();
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await users.AddToRoleAsync((await users.FindByEmailAsync("alice@example.test"))!, RoleNames.Admin)).Succeeded);
        return (await LoginToken()).AccessToken;
    }

    private async Task<AdminLayout> CreateAdminLayout(string token)
    {
        var venue = await CreateAdmin<VenueResponse>("/api/admin/venues", token, new CreateVenueRequest(" Venue ", " Address "));
        Assert.Equal("Venue", venue.Name);
        var hall = await CreateAdmin<AuditoriumResponse>("/api/admin/auditoriums", token, new CreateAuditoriumRequest(venue.Id, "Hall"));
        var seats = await CreateAdmin<SeatResponse[]>($"/api/admin/auditoriums/{hall.Id}/seats", token,
            new AddSeatsRequest([new("A", 1), new("A", 2, 1)]));
        var offering = await CreateAdmin<EventResponse>("/api/admin/events", token, new CreateEventRequest("Film", "Description", 90, 12, 1));
        return new(venue, hall, seats, offering);
    }

    private CreateSessionRequest SessionRequest(AdminLayout setup) => new(setup.Event.Id, setup.Hall.Id, clock.GetUtcNow().AddHours(2), 100m, "UAH");
    private async Task<T> CreateAdmin<T>(string path, string token, object body)
    {
        using var response = await AdminRequest(HttpMethod.Post, path, token, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private async Task<HttpResponseMessage> AdminRequest(HttpMethod method, string path, string? token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    private sealed record AdminLayout(VenueResponse Venue, AuditoriumResponse Hall, SeatResponse[] Seats, EventResponse Event);
    private sealed class AdminInventoryWriteFailure : SaveChangesInterceptor
    {
        private int failNext;
        public void FailNext() => Interlocked.Exchange(ref failNext, 1);
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<SessionSeat>().Any() && Interlocked.Exchange(ref failNext, 0) == 1)
                throw new InvalidOperationException("Test-only inventory write failure.");
            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }
    }
}
