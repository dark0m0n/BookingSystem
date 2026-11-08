using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Administration;
using TicketBooking.Contracts.Auth;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresFact]
    public async Task HoldOwnerGet_ReturnsHistoricalDtoAndLocationWithoutInternalOwnershipData()
    {
        var setup = await OwnedHold();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var seat = await db.SessionSeats.FirstAsync();
            db.Entry(seat).ComplexProperty(value => value.Price).Property(value => value.Amount).CurrentValue = 12m;
            await db.SaveChangesAsync();
        }
        using var response = await AdminRequest(HttpMethod.Get, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var payload = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<HoldResponse>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(setup.Hold.Id, result.Id);
        Assert.Equal(setup.Hold.SessionId, result.SessionId);
        Assert.Equal(setup.Hold.ExpiresAt, result.ExpiresAt);
        Assert.Equal(setup.Hold.TotalAmount, result.TotalAmount);
        Assert.Equal(setup.Hold.Currency, result.Currency);
        Assert.Equal(setup.Hold.Items, result.Items);
        using var document = JsonDocument.Parse(payload);
        Assert.False(document.RootElement.TryGetProperty("userId", out _));
        Assert.DoesNotContain("allocationVersion", payload, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresTheory]
    [InlineData("GET")]
    [InlineData("DELETE")]
    public async Task HoldOwnerRoutes_ChallengeAnonymousAndReturnMissingResourceProblems(string method)
    {
        var path = $"/api/holds/{Guid.NewGuid()}";
        using var anonymous = await AdminRequest(new HttpMethod(method), path, null);
        await AssertProblem(anonymous, 401, "unauthorized");
        var token = await AdminToken();
        using var missing = await AdminRequest(new HttpMethod(method), path, token);
        await AssertProblem(missing, 404, "not_found");
    }

    [PostgresTheory]
    [InlineData("GET", false)]
    [InlineData("DELETE", false)]
    [InlineData("GET", true)]
    [InlineData("DELETE", true)]
    public async Task HoldOwnerRoutes_RejectAnotherCustomerOrAdminWithoutMutation(string method, bool useAdmin)
    {
        var setup = await OwnedHold();
        var token = setup.Admin;
        if (!useAdmin)
        {
            using var registered = await Register("other@example.test", "Other");
            using var login = await Login("other@example.test", Password);
            token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        }
        // A submitted owner field cannot authorize another caller.
        using var response = await AdminRequest(new HttpMethod(method), $"/api/holds/{setup.Hold.Id}?userId={setup.OwnerId}", token);
        await AssertProblem(response, 403, "resource_not_owned");
        Assert.True(response.Headers.CacheControl!.NoStore);
        await AssertOriginalHoldStillAllocated(setup.Hold);
    }

    [PostgresFact]
    public async Task HoldOwnerDelete_CancelsAndReleasesAtomicallyAndCannotReleaseAReacquiredSeat()
    {
        var setup = await OwnedHold();
        using var deleted = await AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Empty(await deleted.Content.ReadAsStringAsync());
        Assert.True(deleted.Headers.CacheControl!.NoStore);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var historical = await db.SeatHolds.Include(value => value.Items).SingleAsync();
            Assert.Equal(SeatHoldStatus.Cancelled, historical.Status);
            Assert.Equal(setup.Hold.ExpiresAt, historical.ExpiresAt);
            Assert.Equal(setup.Hold.TotalAmount, historical.TotalAmount.Amount);
            Assert.Equal(2, historical.Items.Count);
            Assert.All(historical.Items, item => Assert.Equal(1, item.AllocationVersion));
            Assert.All(await db.SessionSeats.ToArrayAsync(), seat =>
            {
                Assert.Equal(SessionSeatStatus.Available, seat.Status);
                Assert.Equal(2, seat.Version);
            });
        }
        using var created = await AdminRequest(HttpMethod.Post, $"/api/sessions/{setup.Session.Id}/holds", setup.Owner,
            new CreateHoldRequest(setup.Hold.Items.Select(item => item.SessionSeatId).ToArray()));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var replacement = (await created.Content.ReadFromJsonAsync<HoldResponse>())!;
        using var repeated = await AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        using var history = await AdminRequest(HttpMethod.Get, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        Assert.Equal((int)SeatHoldStatus.Cancelled, (await history.Content.ReadFromJsonAsync<HoldResponse>())!.Status);
        await using var check = app.Services.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.All(await database.SessionSeats.ToArrayAsync(), seat =>
        {
            Assert.Equal(SessionSeatStatus.Held, seat.Status);
            Assert.Equal(3, seat.Version);
        });
        Assert.All(await database.SeatHoldItems.Where(item => item.SeatHoldId == replacement.Id).ToArrayAsync(),
            item => Assert.Equal(3, item.AllocationVersion));
    }

    [PostgresTheory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task HoldOwnerDelete_EnforcesExactExpiryBoundary(int ticks)
    {
        var setup = await OwnedHold();
        clock.Set(setup.Hold.ExpiresAt.AddTicks(ticks));
        using var response = await AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        if (ticks < 0)
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        else
        {
            await AssertProblem(response, 409, "hold_expired");
            await AssertOriginalHoldStillAllocated(setup.Hold);
            using var get = await AdminRequest(HttpMethod.Get, $"/api/holds/{setup.Hold.Id}", setup.Owner);
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            Assert.Equal((int)SeatHoldStatus.Active, (await get.Content.ReadFromJsonAsync<HoldResponse>())!.Status);
        }
    }

    [PostgresTheory]
    [InlineData(SeatHoldStatus.Converted, "hold_not_active")]
    [InlineData(SeatHoldStatus.Cancelled, "hold_not_active")]
    [InlineData(SeatHoldStatus.Expired, "hold_expired")]
    public async Task HoldOwnerRoutes_ReadTerminalHistoryButRejectCancellation(SeatHoldStatus status, string code)
    {
        var setup = await OwnedHold();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var hold = await db.SeatHolds.SingleAsync();
            if (status == SeatHoldStatus.Expired) { clock.Set(hold.ExpiresAt); hold.Expire(clock); }
            else if (status == SeatHoldStatus.Converted) hold.Convert(hold.UserId, clock);
            else hold.Cancel(hold.UserId, clock);
            await db.SaveChangesAsync();
        }
        using var get = await AdminRequest(HttpMethod.Get, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal((int)status, (await get.Content.ReadFromJsonAsync<HoldResponse>())!.Status);
        using var delete = await AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        if (status == SeatHoldStatus.Cancelled) Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        else await AssertProblem(delete, 409, code);
        await using var check = app.Services.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Equal(status, (await database.SeatHolds.SingleAsync()).Status);
        Assert.All(await database.SessionSeats.ToArrayAsync(), seat =>
        {
            Assert.Equal(SessionSeatStatus.Held, seat.Status);
            Assert.Equal(1, seat.Version);
        });
    }

    [PostgresTheory]
    [InlineData("legacy")]
    [InlineData("transferred")]
    [InlineData("booked")]
    public async Task HoldOwnerDelete_RejectsUnknownOrStaleTokensWithoutPartialRelease(string scenario)
    {
        var setup = await OwnedHold();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var last = await db.SessionSeats.OrderByDescending(seat => seat.Id).FirstAsync();
        if (scenario == "legacy")
        {
            var item = await db.SeatHoldItems.SingleAsync(value => value.SessionSeatId == last.Id);
            db.Entry(item).Property(value => value.AllocationVersion).CurrentValue = null;
            await db.SaveChangesAsync();
        }
        else if (scenario == "transferred")
            Assert.Equal(2, await new SessionSeatAllocationWriter(db).TransferToBookingAsync(last.Id, 1));
        else
            Assert.Equal(2, await new SessionSeatAllocationWriter(db).ConfirmAsync(last.Id, 1));
        var before = await db.SessionSeats.AsNoTracking().ToDictionaryAsync(seat => seat.Id, seat => new { seat.Status, seat.Version });
        using var response = await AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        await AssertProblem(response, 409, "seat_not_available");
        db.ChangeTracker.Clear();
        Assert.Equal(SeatHoldStatus.Active, (await db.SeatHolds.SingleAsync()).Status);
        Assert.Equal(2, await db.SeatHoldItems.CountAsync());
        Assert.All(await db.SessionSeats.ToArrayAsync(), seat =>
        {
            Assert.Equal(before[seat.Id].Status, seat.Status);
            Assert.Equal(before[seat.Id].Version, seat.Version);
        });
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HoldOwnerDelete_SaveFailureOrExpiryRollsBackReleasesAndState(bool expireDuringSave)
    {
        var setup = await OwnedHold();
        holdWriteFailure.FailCancellation = !expireDuringSave;
        if (expireDuringSave) holdWriteFailure.OnCancellationSaving = () => clock.Set(setup.Hold.ExpiresAt);
        using var response = await AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        await AssertProblem(response, expireDuringSave ? 409 : 500, expireDuringSave ? "hold_expired" : "internal_error");
        await AssertOriginalHoldStillAllocated(setup.Hold);
    }

    [PostgresFact]
    public async Task HoldOwnerDelete_ConcurrentCancellationReleasesOnlyOnce()
    {
        var setup = await OwnedHold();
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner)));
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.NoContent, response.StatusCode));
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            Assert.Equal(SeatHoldStatus.Cancelled, (await db.SeatHolds.SingleAsync()).Status);
            Assert.All(await db.SessionSeats.ToArrayAsync(), seat => Assert.Equal(2, seat.Version));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [PostgresFact]
    public async Task HoldOwnerDelete_CancelledSessionReleasesToUnavailable()
    {
        var setup = await OwnedHold();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var session = await db.Sessions.SingleAsync();
        db.Entry(session).Property(value => value.Status).CurrentValue = SessionStatus.Cancelled;
        await db.SaveChangesAsync();
        using var response = await AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.All(await db.SessionSeats.AsNoTracking().ToArrayAsync(), seat =>
        {
            Assert.Equal(SessionSeatStatus.Unavailable, seat.Status);
            Assert.Equal(2, seat.Version);
        });
    }

    [PostgresFact]
    public async Task HoldOwnerRoutes_OpenApiDocumentsAccessAndCancellationProblems()
    {
        using var response = await client.GetAsync("/openapi/v1.json");
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        var path = document.GetProperty("paths").GetProperty("/api/holds/{holdId}");
        foreach (var method in new[] { "get", "delete" })
        {
            var responses = path.GetProperty(method).GetProperty("responses");
            foreach (var status in new[] { "401", "403", "404", "500" })
                Assert.True(responses.TryGetProperty(status, out _));
            Assert.Contains("resource_not_owned", responses.GetProperty("403").GetProperty("description").GetString());
        }
        Assert.True(path.GetProperty("get").GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(path.GetProperty("delete").GetProperty("responses").TryGetProperty("204", out _));
        Assert.Contains("hold_expired", path.GetProperty("delete").GetProperty("responses").GetProperty("409").GetProperty("description").GetString());
    }

    private async Task<(string Admin, string Owner, HoldResponse Hold, SessionResponse Session, Guid OwnerId)> OwnedHold()
    {
        var (admin, session) = await HoldSession();
        using var registered = await Register("owner@example.test", "Owner");
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        using var login = await Login("owner@example.test", Password);
        var credentials = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        var owner = credentials.AccessToken;
        using var created = await AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", owner,
            new CreateHoldRequest(session.Seats.Select(seat => seat.Id).ToArray()));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var hold = (await created.Content.ReadFromJsonAsync<HoldResponse>())!;
        Assert.Equal($"/api/holds/{hold.Id}", created.Headers.Location!.OriginalString);
        return (admin, owner, hold, session, credentials.User.Id);
    }

    private async Task AssertOriginalHoldStillAllocated(HoldResponse hold)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var stored = await db.SeatHolds.Include(value => value.Items).SingleAsync();
        Assert.Equal(hold.Id, stored.Id);
        Assert.Equal(SeatHoldStatus.Active, stored.Status);
        Assert.Equal(hold.ExpiresAt, stored.ExpiresAt);
        Assert.Equal(hold.TotalAmount, stored.TotalAmount.Amount);
        Assert.Equal(hold.Items.Count, stored.Items.Count);
        Assert.All(await db.SessionSeats.ToArrayAsync(), seat =>
        {
            Assert.Equal(SessionSeatStatus.Held, seat.Status);
            Assert.Equal(1, seat.Version);
        });
    }
}
