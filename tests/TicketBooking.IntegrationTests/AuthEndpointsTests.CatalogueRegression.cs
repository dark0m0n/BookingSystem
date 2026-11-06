using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TicketBooking.Contracts.Administration;
using TicketBooking.Contracts.Auth;
using TicketBooking.Contracts.Catalogue;
using TicketBooking.Domain;
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
    public async Task CatalogueRegression_CombinedFiltersPaginateDistinctEventsAndCountAllMatches()
    {
        var venueId = await SeedPublicEvents();
        Guid[] expected;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var hallId = await db.Auditoriums.Where(a => a.VenueId == venueId).Select(a => a.Id).SingleAsync();
            var alpha = await db.Events.SingleAsync(e => e.Title == "Alpha");
            var additions = new[] { "Alpha sequel", "Zeta space" }.Select(title =>
                new Event(Guid.NewGuid(), title, "SPACE story", TimeSpan.FromMinutes(90), 0, EventStatus.Published)).ToArray();
            var day = new DateTimeOffset(2030, 1, 2, 12, 0, 0, TimeSpan.Zero);
            foreach (var offering in additions)
            {
                db.Events.Add(offering);
                db.Sessions.Add(new Session(Guid.NewGuid(), offering.Id, hallId, day, day.AddMinutes(90)));
            }
            await db.SaveChangesAsync();
            expected = new[] { alpha.Id }.Concat(additions.Select(e => e.Id)).ToArray();
        }
        var returned = new List<Guid>();
        for (var pageNumber = 1; pageNumber <= 4; pageNumber++)
        {
            var page = await ReadPublicEvents($"query=%20sPaCe%20&date=2030-01-02&venueId={venueId}&page={pageNumber}&pageSize=1");
            Assert.Equal(3, page.TotalCount);
            Assert.Equal(3, page.TotalPages);
            Assert.Equal(pageNumber > 1, page.HasPreviousPage);
            Assert.Equal(pageNumber < 3, page.HasNextPage);
            Assert.Equal(pageNumber <= 3 ? 1 : 0, page.Items.Count);
            returned.AddRange(page.Items.Select(e => e.Id));
        }
        Assert.Equal(expected, returned);
        Assert.Equal(returned.Count, returned.Distinct().Count());
    }

    [PostgresFact]
    public async Task CatalogueRegression_CompoundInvalidFiltersReturnAllFieldErrorsWithoutChangingStoredData()
    {
        await SeedPublicEvents();
        Guid eventId;
        int eventCount;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            eventId = await db.Events.Where(e => e.Title == "Alpha").Select(e => e.Id).SingleAsync();
            eventCount = await db.Events.CountAsync();
        }
        var before = await CaptureEditState();
        foreach (var path in new[] { "/api/events", $"/api/events/{eventId}/sessions" })
        {
            var query = "date=2030-02-30&venueId=invalid&page=0&pageSize=101";
            if (path == "/api/events") query += "&query=" + new string('x', 201);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{path}?{query}");
            request.Headers.Accept.ParseAdd("application/xml");
            using var response = await client.SendAsync(request);
            var problem = await AssertProblem(response, 400, "validation_failed");
            var fields = problem.GetProperty("errors").EnumerateObject().Select(p => p.Name);
            var expected = new[] { "date", "venueId", "page", "pageSize" }.Concat(path == "/api/events" ? ["query"] : Array.Empty<string>());
            Assert.Equal(expected.Order(), fields.Order());
            Assert.Equal(before, await CaptureEditState());
        }
        await using var check = app.Services.CreateAsyncScope();
        Assert.Equal(eventCount, await check.ServiceProvider.GetRequiredService<TicketBookingDbContext>().Events.CountAsync());
    }

    [PostgresFact]
    public async Task CatalogueRegression_AdminRoutesRejectRolelessTamperedAndExpiredTokensBeforeWrites()
    {
        using var registered = await Register();
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync("alice@example.test"))!;
            Assert.True((await users.RemoveFromRoleAsync(user, RoleNames.Customer)).Succeeded);
        }
        var roleless = await LoginToken();
        var token = new JwtSecurityTokenHandler().ReadJwtToken(roleless.AccessToken);
        token.Payload["role"] = RoleNames.Admin;
        var parts = roleless.AccessToken.Split('.');
        var forged = parts[0] + "." + Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(token.Payload)) + "." + parts[2];
        foreach (var (method, path, body) in AdministrationRequests(Guid.NewGuid()))
        {
            using var denied = await AdminRequest(method, path, roleless.AccessToken, body);
            await AssertProblem(denied, 403, "forbidden");
            using var tampered = await AdminRequest(method, path, forged, body);
            await AssertProblem(tampered, 401, "unauthorized");
        }
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            Assert.Empty(await db.Venues.ToArrayAsync());
            Assert.Empty(await db.Events.ToArrayAsync());
            Assert.Empty(await db.Sessions.ToArrayAsync());
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.AddToRoleAsync((await users.FindByEmailAsync("alice@example.test"))!, RoleNames.Admin)).Succeeded);
        }
        var admin = await LoginToken();
        var created = await CreateAdmin<VenueResponse>("/api/admin/venues", admin.AccessToken, new CreateVenueRequest("Allowed", "Address"));
        Assert.NotEqual(Guid.Empty, created.Id);
        clock.Set(admin.ExpiresAt);
        foreach (var (method, path, body) in AdministrationRequests(Guid.NewGuid()))
        {
            using var expired = await AdminRequest(method, path, admin.AccessToken, body);
            await AssertProblem(expired, 401, "unauthorized");
        }
        await using var final = app.Services.CreateAsyncScope();
        Assert.Equal(1, await final.ServiceProvider.GetRequiredService<TicketBookingDbContext>().Venues.CountAsync());
    }

    [PostgresTheory]
    [InlineData("UAH", "0")]
    [InlineData("EUR", "0.01")]
    [InlineData("USD", "9999999999999999.99")]
    public async Task CatalogueRegression_PublicSeatMapsPreserveExactPriceLimitsForGuestsAndCustomers(string currency, string amount)
    {
        var admin = await AdminToken();
        var setup = await CreateAdminLayout(admin);
        var price = decimal.Parse(amount, CultureInfo.InvariantCulture);
        var session = await CreateAdmin<SessionResponse>("/api/admin/sessions", admin,
            SessionRequest(setup) with { Price = price, Currency = currency });
        using var registration = await Register("viewer@example.test");
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var login = await Login("viewer@example.test", Password);
        var customer = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        var path = $"/api/sessions/{session.Id}/seats";
        using var guest = await client.GetAsync(path);
        using var signedIn = await AdminRequest(HttpMethod.Get, path, customer);
        Assert.Equal(HttpStatusCode.OK, guest.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.True(guest.Headers.CacheControl?.NoStore);
        Assert.True(signedIn.Headers.CacheControl?.NoStore);
        var guestMap = (await guest.Content.ReadFromJsonAsync<SessionSeatMapResponse>())!;
        var customerMap = (await signedIn.Content.ReadFromJsonAsync<SessionSeatMapResponse>())!;
        Assert.Equal(guestMap.Seats, customerMap.Seats);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var stored = await db.SessionSeats.AsNoTracking().Where(s => s.SessionId == session.Id).ToDictionaryAsync(s => s.Id);
        Assert.Equal(stored.Count, guestMap.Seats.Count);
        Assert.All(guestMap.Seats, s =>
        {
            Assert.Equal(price, s.Price);
            Assert.Equal(currency, s.Currency);
            Assert.Equal(stored[s.Id].Price.Amount, s.Price);
            Assert.Equal(stored[s.Id].SeatId, s.SeatId);
            Assert.Equal((int)stored[s.Id].Status, s.Status);
            Assert.Equal(stored[s.Id].Version, s.Version);
        });
    }

    [PostgresFact]
    public async Task CatalogueRegression_SeatMapReadAtHoldExpiryDoesNotReleaseInventoryOrRewriteHistory()
    {
        var admin = await AdminToken();
        var setup = await CreateAdminLayout(admin);
        var session = await CreateAdmin<SessionResponse>("/api/admin/sessions", admin, SessionRequest(setup));
        DateTimeOffset expiration;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var seat = await db.SessionSeats.SingleAsync(s => s.Id == session.Seats[0].Id);
            var hold = new SeatHold(Guid.NewGuid(), session.Id, Guid.NewGuid(), session.StartsAt, [seat], clock, EditProtectionTiming());
            await using var transaction = await db.Database.BeginTransactionAsync();
            await new SessionSeatAllocationWriter(db).HoldAsync(seat.Id, seat.Version);
            db.SeatHolds.Add(hold);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            expiration = await db.SeatHolds.Where(h => h.Id == hold.Id).Select(h => h.ExpiresAt).SingleAsync();
        }
        clock.Set(expiration);
        var before = await CaptureEditState();
        using var response = await client.GetAsync($"/api/sessions/{session.Id}/seats");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var map = (await response.Content.ReadFromJsonAsync<SessionSeatMapResponse>())!;
        var held = Assert.Single(map.Seats, s => s.Id == session.Seats[0].Id);
        Assert.Equal((int)SessionSeatStatus.Held, held.Status);
        Assert.Equal(1, held.Version);
        Assert.Equal(before, await CaptureEditState());
    }
}
