using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Auth;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Identity;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Identity;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresFact]
    public async Task HoldLifecycle_LookupCancellationAndExpiryStayIsolatedAcrossOneOwnersHolds()
    {
        var (admin, session) = await HoldSession();
        using var registered = await Register("lookup-owner@example.test", "Lookup owner");
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        using var login = await Login("lookup-owner@example.test", Password);
        var owner = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var inventory = await db.SessionSeats.OrderBy(seat => seat.Id).ToArrayAsync();
        db.Entry(inventory[1]).ComplexProperty(seat => seat.Price).Property(price => price.Amount).CurrentValue = 75.25m;
        db.Entry(inventory[1]).ComplexProperty(seat => seat.Price).Property(price => price.Currency).CurrentValue = "EUR";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var holds = new List<HoldResponse>();
        foreach (var seat in inventory)
        {
            using var created = await AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", owner,
                new CreateHoldRequest([seat.Id]));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var hold = (await created.Content.ReadFromJsonAsync<HoldResponse>())!;
            holds.Add(hold);
            using var found = await AdminRequest(HttpMethod.Get, created.Headers.Location!.OriginalString, owner);
            Assert.Equal(HttpStatusCode.OK, found.StatusCode);
            Assert.True(found.Headers.CacheControl!.NoStore);
            var result = (await found.Content.ReadFromJsonAsync<HoldResponse>())!;
            Assert.Equal(hold.Id, result.Id);
            Assert.Equal(seat.Id, Assert.Single(result.Items).SessionSeatId);
            Assert.Equal(seat.Price.Amount, result.TotalAmount);
            Assert.Equal(seat.Price.Currency, result.Currency);
            using var denied = await AdminRequest(HttpMethod.Get, $"/api/holds/{hold.Id}", admin);
            await AssertProblem(denied, 403, "resource_not_owned");
        }
        Assert.NotEqual(holds[0].Id, holds[1].Id);
        using var missing = await AdminRequest(HttpMethod.Get, $"/api/holds/{Guid.NewGuid()}", owner);
        await AssertProblem(missing, 404, "not_found");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var cancelled = await AdminRequest(HttpMethod.Delete, $"/api/holds/{holds[0].Id}", owner);
            Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        }
        using (var remaining = await AdminRequest(HttpMethod.Get, $"/api/holds/{holds[1].Id}", owner))
        {
            var active = (await remaining.Content.ReadFromJsonAsync<HoldResponse>())!;
            Assert.Equal((int)SeatHoldStatus.Active, active.Status);
            Assert.Equal(75.25m, active.TotalAmount);
            Assert.Equal("EUR", active.Currency);
        }
        var beforeExpiry = await db.SessionSeats.AsNoTracking().ToDictionaryAsync(seat => seat.Id);
        Assert.Equal(SessionSeatStatus.Available, beforeExpiry[inventory[0].Id].Status);
        Assert.Equal(2, beforeExpiry[inventory[0].Id].Version);
        Assert.Equal(SessionSeatStatus.Held, beforeExpiry[inventory[1].Id].Status);
        Assert.Equal(2, beforeExpiry[inventory[1].Id].Version);
        clock.Set(holds[1].ExpiresAt);
        Assert.Equal(1, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        Assert.Equal(0, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        for (var index = 0; index < holds.Count; index++)
        {
            using var response = await AdminRequest(HttpMethod.Get, $"/api/holds/{holds[index].Id}", owner);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var history = (await response.Content.ReadFromJsonAsync<HoldResponse>())!;
            Assert.Equal(index == 0 ? (int)SeatHoldStatus.Cancelled : (int)SeatHoldStatus.Expired, history.Status);
            Assert.Equal(holds[index].ExpiresAt, history.ExpiresAt);
            Assert.Equal(holds[index].Items, history.Items);
            Assert.Equal(holds[index].TotalAmount, history.TotalAmount);
            Assert.Equal(holds[index].Currency, history.Currency);
        }
        var final = await db.SessionSeats.AsNoTracking().ToDictionaryAsync(seat => seat.Id);
        Assert.All(final.Values, seat => Assert.Equal(SessionSeatStatus.Available, seat.Status));
        Assert.Equal(2, final[inventory[0].Id].Version);
        Assert.Equal(3, final[inventory[1].Id].Version);
        Assert.Equal(2, await db.SeatHolds.CountAsync());
        Assert.Equal(2, await db.SeatHoldItems.CountAsync());
    }

    [PostgresTheory]
    [InlineData("GET")]
    [InlineData("DELETE")]
    public async Task HoldLifecycle_StoredOwnerWithoutCustomerPolicyCannotReadOrCancel(string method)
    {
        var setup = await OwnedHold();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(setup.OwnerId.ToString()))!;
            Assert.True((await users.RemoveFromRoleAsync(user, RoleNames.Customer)).Succeeded);
        }
        using var login = await Login("owner@example.test", Password);
        var roleless = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        using var response = await AdminRequest(new HttpMethod(method), $"/api/holds/{setup.Hold.Id}", roleless);
        await AssertProblem(response, 403, "forbidden");
        await AssertOriginalHoldStillAllocated(setup.Hold);
    }

    [PostgresFact]
    public async Task HoldLifecycle_PostgresItemInsertFailureRollsBackHeaderItemsAndEverySeatClaim()
    {
        var (token, session) = await HoldSession();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        // Test-only SQL runs inside this fixture's private schema. Reject actual child inserts after
        // allocation updates/header insertion, exercising the database savepoint and outer rollback.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_multi_seat_hold() RETURNS trigger LANGUAGE plpgsql AS $function$
            BEGIN
                IF (SELECT count(*) FROM "SeatHoldItems" WHERE "SeatHoldId" = NEW."SeatHoldId") > 1 THEN
                    RAISE EXCEPTION USING ERRCODE = '23514', CONSTRAINT = 'test_multi_seat_insert_failure',
                        MESSAGE = 'Injected database item insert failure';
                END IF;
                RETURN NEW;
            END;
            $function$;
            CREATE TRIGGER reject_multi_seat_hold AFTER INSERT ON "SeatHoldItems"
                FOR EACH ROW EXECUTE FUNCTION reject_multi_seat_hold();
            """);
        using var response = await AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", token,
            new CreateHoldRequest(session.Seats.Select(seat => seat.Id).ToArray()));
        var problem = await AssertProblem(response, 500, "internal_error");
        Assert.DoesNotContain("test_multi_seat_insert_failure", problem.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("Injected database", problem.GetRawText(), StringComparison.Ordinal);
        await AssertNoHoldAllocation();
        // Each request remains independent: the failed multi-seat transaction did not poison inventory.
        using var retry = await AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", token,
            new CreateHoldRequest([session.Seats[0].Id]));
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(1, await db.SeatHolds.CountAsync());
        Assert.Equal(1, await db.SeatHoldItems.CountAsync());
        var seats = await db.SessionSeats.AsNoTracking().ToArrayAsync();
        Assert.Equal(1, seats.Single(seat => seat.Id == session.Seats[0].Id).Version);
        Assert.Equal(0, seats.Single(seat => seat.Id == session.Seats[1].Id).Version);
    }
}
