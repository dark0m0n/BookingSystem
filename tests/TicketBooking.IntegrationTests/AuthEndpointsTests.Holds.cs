using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Administration;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    private readonly HoldWriteFailure holdWriteFailure = new();

    [PostgresFact]
    public async Task CustomerHold_CapturesAuthenticatedOwnerServerPricesDeadlineAndAllocationVersions()
    {
        var (admin, session) = await HoldSession();
        using var registration = await Register("customer@example.test", "Customer");
        var login = (await (await Login("customer@example.test", Password)).Content.ReadFromJsonAsync<TicketBooking.Contracts.Auth.LoginResponse>())!;
        var ids = session.Seats.Select(seat => seat.Id).ToArray();
        using var response = await AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", login.AccessToken,
            new { sessionSeatIds = ids, userId = Guid.NewGuid(), totalAmount = 1, currency = "USD", expiresAt = clock.GetUtcNow().AddDays(1) });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var payload = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<HoldResponse>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(session.Id, result.SessionId);
        Assert.Equal(0, result.Status);
        Assert.Equal(200m, result.TotalAmount);
        Assert.Equal("UAH", result.Currency);
        Assert.InRange(result.ExpiresAt, clock.GetUtcNow().AddMinutes(10).AddTicks(-9), clock.GetUtcNow().AddMinutes(10));
        Assert.Equal(TimeSpan.Zero, result.ExpiresAt.Offset);
        Assert.Equal(ids.Order(), result.Items.Select(item => item.SessionSeatId).Order());
        Assert.All(result.Items, item => { Assert.Equal(100m, item.Price); Assert.Equal("UAH", item.Currency); });
        using var document = JsonDocument.Parse(payload);
        Assert.False(document.RootElement.TryGetProperty("userId", out _));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var hold = await db.SeatHolds.Include(value => value.Items).SingleAsync();
        Assert.Equal(login.User.Id, hold.UserId);
        Assert.Equal(result.Id, hold.Id);
        Assert.Equal(result.ExpiresAt, hold.ExpiresAt);
        Assert.All(await db.SessionSeats.ToListAsync(), seat =>
        {
            Assert.Equal(SessionSeatStatus.Held, seat.Status);
            Assert.Equal(1, seat.Version);
            Assert.Equal(seat.Version, hold.Items.Single(item => item.SessionSeatId == seat.Id).AllocationVersion);
        });
        using var edit = await AdminRequest(HttpMethod.Patch, $"/api/admin/sessions/{session.Id}", admin,
            new UpdateSessionRequest(StartsAt: clock.GetUtcNow().AddHours(3)));
        await AssertProblem(edit, 409, "conflict");
        using var map = await client.GetAsync($"/api/sessions/{session.Id}/seats");
        Assert.Equal(HttpStatusCode.OK, map.StatusCode);
        Assert.All((await map.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("seats").EnumerateArray(), seat =>
            Assert.Equal((int)SessionSeatStatus.Held, seat.GetProperty("status").GetInt32()));
    }

    [PostgresTheory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"sessionSeatIds\":null}")]
    [InlineData("{\"sessionSeatIds\":[]}")]
    [InlineData("{\"sessionSeatIds\":[\"00000000-0000-0000-0000-000000000000\"]}")]
    [InlineData("{\"sessionSeatIds\":[\"invalid\"]}")]
    [InlineData("{")]
    public async Task CustomerHold_InvalidBodiesReturnValidationProblem(string body)
    {
        var token = await AdminToken();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{Guid.NewGuid()}/holds");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        await AssertProblem(response, 400, "validation_failed");
    }

    [PostgresFact]
    public async Task CustomerHold_RejectsEmptyDuplicateAndOversizedSelectionsWithoutWrites()
    {
        var (token, session) = await HoldSession();
        var id = session.Seats[0].Id;
        foreach (var ids in new[] { Array.Empty<Guid>(), new[] { id, id }, Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray() })
        {
            using var response = await AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", token, new CreateHoldRequest(ids));
            var problem = await AssertProblem(response, 400, "validation_failed");
            Assert.True(problem.GetProperty("errors").TryGetProperty("sessionSeatIds", out _));
            await AssertNoHoldAllocation();
        }
    }

    [PostgresFact]
    public async Task CustomerHold_RequiresAuthenticationAndCustomerPolicy()
    {
        var path = $"/api/sessions/{Guid.NewGuid()}/holds";
        using var anonymous = await client.PostAsJsonAsync(path, new CreateHoldRequest([Guid.NewGuid()]));
        await AssertProblem(anonymous, 401, "unauthorized");
        using var registered = await Register();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<TicketBooking.Infrastructure.Identity.ApplicationUser>>();
            Assert.True((await manager.RemoveFromRoleAsync((await manager.FindByEmailAsync("alice@example.test"))!, "Customer")).Succeeded);
        }
        var token = (await LoginToken()).AccessToken;
        using var forbidden = await AdminRequest(HttpMethod.Post, path, token, new CreateHoldRequest([Guid.NewGuid()]));
        await AssertProblem(forbidden, 403, "forbidden");
    }

    [PostgresFact]
    public async Task CustomerHold_RejectsMissingAndForeignSessionInventoryWithoutWrites()
    {
        var (token, session) = await HoldSession();
        using var missing = await AdminRequest(HttpMethod.Post, $"/api/sessions/{Guid.NewGuid()}/holds", token,
            new CreateHoldRequest([session.Seats[0].Id]));
        await AssertProblem(missing, 404, "not_found");
        using var invalid = await AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", token,
            new CreateHoldRequest([session.Seats[0].Id, Guid.NewGuid()]));
        await AssertProblem(invalid, 404, "not_found");
        await AssertNoHoldAllocation();
    }

    [PostgresTheory]
    [InlineData(SessionSeatStatus.Held, false)]
    [InlineData(SessionSeatStatus.Held, true)]
    [InlineData(SessionSeatStatus.Booked, false)]
    [InlineData(SessionSeatStatus.Booked, true)]
    [InlineData(SessionSeatStatus.Unavailable, false)]
    [InlineData(SessionSeatStatus.Unavailable, true)]
    public async Task CustomerHold_UnavailableSeatRejectsWholeSelection(SessionSeatStatus status, bool reverseSelection)
    {
        var (token, session) = await HoldSession();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var row = await db.SessionSeats.SingleAsync(seat => seat.Id == session.Seats[1].Id);
            db.Entry(row).Property(seat => seat.Status).CurrentValue = status;
            await db.SaveChangesAsync();
        }
        await using var beforeScope = app.Services.CreateAsyncScope();
        var before = await beforeScope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().SessionSeats.AsNoTracking()
            .ToDictionaryAsync(seat => seat.Id, seat => new { seat.Status, seat.Version });
        var selection = session.Seats.Select(seat => seat.Id);
        using var response = await AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", token,
            new CreateHoldRequest((reverseSelection ? selection.Reverse() : selection).ToArray()));
        await AssertProblem(response, 409, "seat_not_available");
        await using var check = app.Services.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Empty(await database.SeatHolds.ToArrayAsync());
        Assert.Empty(await database.SeatHoldItems.ToArrayAsync());
        var after = await database.SessionSeats.ToArrayAsync();
        Assert.Equal(before.Count, after.Length);
        Assert.All(after, seat =>
        {
            Assert.Equal(before[seat.Id].Status, seat.Status);
            Assert.Equal(before[seat.Id].Version, seat.Version);
        });
    }

    [PostgresTheory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task CustomerHold_SessionStartBoundaryCapsOrRejectsHold(int ticksFromStart)
    {
        var (token, session) = await HoldSession();
        clock.Set(session.StartsAt.AddTicks(ticksFromStart));
        token = (await LoginToken()).AccessToken;
        using var response = await AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", token,
            new CreateHoldRequest([session.Seats[0].Id]));
        if (ticksFromStart < 0)
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(session.StartsAt, (await response.Content.ReadFromJsonAsync<HoldResponse>())!.ExpiresAt);
        }
        else
        {
            await AssertProblem(response, 409, "conflict");
            await AssertNoHoldAllocation();
        }
    }

    [PostgresFact]
    public async Task CustomerHold_ConcurrentOverlappingRequestsHaveExactlyOneWinner()
    {
        var (token, session) = await HoldSession();
        var ids = session.Seats.Select(seat => seat.Id).ToArray();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
            AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", token,
                new CreateHoldRequest(index % 2 == 0 ? ids : ids.Reverse().ToArray()))));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            foreach (var response in responses.Where(response => response.StatusCode != HttpStatusCode.Created))
                await AssertProblem(response, 409, "seat_not_available");
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            Assert.Equal(1, await db.SeatHolds.CountAsync());
            Assert.Equal(2, await db.SeatHoldItems.CountAsync());
            Assert.All(await db.SessionSeats.ToArrayAsync(), seat => Assert.Equal(1, seat.Version));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [PostgresFact]
    public async Task CustomerHold_SaveFailureRollsBackEveryClaimAndHoldItem()
    {
        var (token, session) = await HoldSession();
        holdWriteFailure.Enabled = true;
        using var response = await AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", token,
            new CreateHoldRequest(session.Seats.Select(seat => seat.Id).ToArray()));
        await AssertProblem(response, 500, "internal_error");
        await AssertNoHoldAllocation();
    }

    [PostgresFact]
    public async Task CustomerHold_ExpiryDuringSaveRollsBackTheHoldAndAllClaims()
    {
        var (token, session) = await HoldSession();
        holdWriteFailure.OnHoldSaving = () => clock.Set(clock.GetUtcNow().AddMinutes(10));

        using var response = await AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", token,
            new CreateHoldRequest(session.Seats.Select(seat => seat.Id).ToArray()));

        await AssertProblem(response, 409, "hold_expired");
        await AssertNoHoldAllocation();
    }

    [PostgresFact]
    public async Task CustomerHold_ConcurrentAdminReschedulePreservesTheCapturedSessionRelationship()
    {
        var (token, session) = await HoldSession();
        var holdTask = AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", token,
            new CreateHoldRequest([session.Seats[0].Id]));
        var editTask = AdminRequest(HttpMethod.Patch, $"/api/admin/sessions/{session.Id}", token,
            new UpdateSessionRequest(StartsAt: clock.GetUtcNow().AddMinutes(5)));
        using var holdResponse = await holdTask;
        using var editResponse = await editTask;
        Assert.Equal(HttpStatusCode.Created, holdResponse.StatusCode);
        Assert.Contains(editResponse.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        var result = (await holdResponse.Content.ReadFromJsonAsync<HoldResponse>())!;
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var storedSession = await db.Sessions.SingleAsync();
        var hold = await db.SeatHolds.Include(value => value.Items).SingleAsync();
        Assert.Equal(storedSession.Id, hold.SessionId);
        Assert.True(result.ExpiresAt <= storedSession.StartsAt);
        var item = Assert.Single(hold.Items);
        Assert.Equal((await db.SessionSeats.SingleAsync(seat => seat.Id == item.SessionSeatId)).Version, item.AllocationVersion);
    }

    [PostgresTheory]
    [InlineData("draft", 404)]
    [InlineData("archived", 404)]
    [InlineData("cancelled", 409)]
    [InlineData("completed", 409)]
    [InlineData("foreign", 404)]
    [InlineData("foreign_only", 404)]
    [InlineData("mixed_currency", 409)]
    [InlineData("total_overflow", 409)]
    public async Task CustomerHold_RejectsIneligibleSessionsAndInventory(string scenario, int status)
    {
        var (token, session) = await HoldSession();
        var ids = session.Seats.Select(seat => seat.Id).ToArray();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            if (scenario is "draft" or "archived")
            {
                var offering = await db.Events.SingleAsync();
                db.Entry(offering).Property(value => value.Status).CurrentValue = scenario == "draft" ? EventStatus.Draft : EventStatus.Archived;
            }
            else if (scenario is "cancelled" or "completed")
            {
                var stored = await db.Sessions.SingleAsync();
                db.Entry(stored).Property(value => value.Status).CurrentValue = scenario == "cancelled" ? SessionStatus.Cancelled : SessionStatus.Completed;
            }
            else if (scenario is "foreign" or "foreign_only")
            {
                var other = new Session(Guid.NewGuid(), session.EventId, session.AuditoriumId, session.StartsAt, session.EndsAt);
                var seat = new SessionSeat(Guid.NewGuid(), other.Id, session.Seats[0].SeatId, new TicketBooking.Domain.Money(100, "UAH"));
                db.AddRange(other, seat);
                ids = scenario == "foreign" ? [ids[0], seat.Id] : [seat.Id];
            }
            else
            {
                var seats = await db.SessionSeats.OrderBy(value => value.Id).ToArrayAsync();
                if (scenario == "mixed_currency")
                    db.Entry(seats[0]).ComplexProperty(value => value.Price).Property(value => value.Currency).CurrentValue = "EUR";
                else
                    foreach (var seat in seats)
                        db.Entry(seat).ComplexProperty(value => value.Price).Property(value => value.Amount).CurrentValue = TicketBooking.Domain.Money.MaxAmount;
            }
            await db.SaveChangesAsync();
        }
        await using var beforeScope = app.Services.CreateAsyncScope();
        var before = await beforeScope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().SessionSeats.AsNoTracking()
            .ToDictionaryAsync(seat => seat.Id, seat => seat.Version);
        using var response = await AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", token, new CreateHoldRequest(ids));
        await AssertProblem(response, status, status == 404 ? "not_found" : "conflict");
        await using var check = app.Services.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Empty(await database.SeatHolds.ToArrayAsync());
        Assert.Empty(await database.SeatHoldItems.ToArrayAsync());
        Assert.All(await database.SessionSeats.ToArrayAsync(), seat =>
        {
            Assert.Equal(SessionSeatStatus.Available, seat.Status);
            Assert.Equal(before[seat.Id], seat.Version);
        });
    }

    [PostgresFact]
    public async Task CustomerHold_OpenApiDocumentsRequestResponseAndProblems()
    {
        using var response = await client.GetAsync("/openapi/v1.json");
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        var operation = document.GetProperty("paths").GetProperty("/api/sessions/{sessionId}/holds").GetProperty("post");
        var responses = operation.GetProperty("responses");
        foreach (var status in new[] { "201", "400", "401", "403", "404", "409", "415", "500" })
            Assert.True(responses.TryGetProperty(status, out _), status);
        Assert.Contains("seat_not_available", responses.GetProperty("409").GetProperty("description").GetString());
        Assert.True(responses.GetProperty("409").GetProperty("content").TryGetProperty("application/problem+json", out _));
    }

    private async Task<(string Token, SessionResponse Session)> HoldSession()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        return (token, await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup)));
    }

    private async Task AssertNoHoldAllocation()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Empty(await db.SeatHolds.ToArrayAsync());
        Assert.Empty(await db.SeatHoldItems.ToArrayAsync());
        Assert.All(await db.SessionSeats.ToArrayAsync(), seat =>
        {
            Assert.Equal(SessionSeatStatus.Available, seat.Status);
            Assert.Equal(0, seat.Version);
        });
    }

    private sealed class HoldWriteFailure : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public bool FailCancellation { get; set; }
        public bool FailConversion { get; set; }
        public Guid? FailingExpiryId { get; set; }
        public Action? OnHoldSaving { get; set; }
        public Action? OnCancellationSaving { get; set; }
        public Action? OnExpirySaving { get; set; }
        public Action? OnConversionSaving { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<SeatHold>().Any(entry => entry.State == EntityState.Added))
            {
                if (Enabled) throw new InvalidOperationException("Injected hold persistence failure.");
                OnHoldSaving?.Invoke();
            }
            if (eventData.Context!.ChangeTracker.Entries<SeatHold>().Any(entry =>
                entry.State == EntityState.Modified && entry.Entity.Status == SeatHoldStatus.Cancelled))
            {
                if (FailCancellation) throw new InvalidOperationException("Injected cancellation persistence failure.");
                OnCancellationSaving?.Invoke();
            }
            var expiring = eventData.Context!.ChangeTracker.Entries<SeatHold>().Where(entry =>
                entry.State == EntityState.Modified && entry.Entity.Status == SeatHoldStatus.Expired).ToArray();
            if (expiring.Length > 0)
            {
                if (expiring.Any(entry => entry.Entity.Id == FailingExpiryId))
                    throw new InvalidOperationException("Injected expiry persistence failure.");
                OnExpirySaving?.Invoke();
            }
            if (eventData.Context!.ChangeTracker.Entries<SeatHold>().Any(entry =>
                entry.State == EntityState.Modified && entry.Entity.Status == SeatHoldStatus.Converted))
            {
                if (FailConversion) throw new InvalidOperationException("Injected conversion persistence failure.");
                OnConversionSaving?.Invoke();
            }
            return ValueTask.FromResult(result);
        }
    }
}
