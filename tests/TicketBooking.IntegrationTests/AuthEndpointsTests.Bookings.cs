using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Booking;
using TicketBooking.Domain.Booking;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Identity;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresFact]
    public async Task CustomerBooking_ExistingSourceHoldConstraintRollsBackASecondConversion()
    {
        var setup = await OwnedHold();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var hold = await db.SeatHolds.Include(value => value.Items).SingleAsync();
            // Simulate an inconsistent writer that persisted a booking but left its hold Active.
            db.Bookings.Add(TicketBooking.Domain.Booking.Booking.CreateFromHold(Guid.NewGuid(), hold, setup.OwnerId,
                "BK-EXISTING", clock, setup.Session.StartsAt,
                scope.ServiceProvider.GetRequiredService<TicketBooking.Domain.BookingTimingPolicy>()));
            await db.SaveChangesAsync();
        }

        using var response = await ConvertHold(setup.Hold.Id, setup.Owner);
        await AssertProblem(response, 409, "hold_not_active");
        await AssertOriginalHoldStillAllocated(setup.Hold);
        await using var check = app.Services.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var booking = await database.Bookings.SingleAsync();
        Assert.Equal("BK-EXISTING", booking.Number);
        Assert.Equal(setup.Hold.Id, booking.SourceHoldId);
        Assert.Equal(2, await database.BookingItems.CountAsync());
    }

    [PostgresFact]
    public async Task CustomerBooking_ReturnsServerIdentityHistoricalPricesAndCapturedDeadlines()
    {
        clock.Set(new DateTimeOffset(clock.GetUtcNow().UtcTicks / 10 * 10 + 7, TimeSpan.Zero));
        var setup = await OwnedHold();
        using var response = await AdminRequest(HttpMethod.Post, "/api/bookings", setup.Owner, new
        {
            holdId = setup.Hold.Id,
            userId = Guid.NewGuid(),
            number = "CLIENT-NUMBER",
            totalAmount = 1m,
            currency = "USD"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var payload = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BookingResponse>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("BK-" + result.Id.ToString("N"), result.Number);
        Assert.Equal(setup.Session.Id, result.SessionId);
        Assert.Equal((int)BookingStatus.PendingPayment, result.Status);
        Assert.Equal(setup.Hold.TotalAmount, result.TotalAmount);
        Assert.Equal(setup.Hold.Currency, result.Currency);
        Assert.Equal(new DateTimeOffset(clock.GetUtcNow().UtcTicks / 10 * 10, TimeSpan.Zero), result.CreatedAt);
        Assert.Equal(setup.Session.StartsAt, result.SessionStartsAt);
        Assert.Equal(result.CreatedAt.AddMinutes(15), result.PaymentDeadline);
        Assert.Equal(setup.Session.StartsAt.AddHours(-1), result.CancellationDeadline);
        Assert.Equal(setup.Hold.Items.OrderBy(item => item.SessionSeatId).Select(item => (item.SessionSeatId, item.Price, item.Currency)),
            result.Items.OrderBy(item => item.SessionSeatId).Select(item => (item.SessionSeatId, item.Price, item.Currency)));
        using var document = JsonDocument.Parse(payload);
        Assert.False(document.RootElement.TryGetProperty("userId", out _));
        Assert.DoesNotContain("allocationVersion", payload, StringComparison.OrdinalIgnoreCase);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var booking = await db.Bookings.SingleAsync();
        Assert.Equal(result.Id, booking.Id);
        Assert.Equal(setup.OwnerId, booking.UserId);
        Assert.Equal(result.Number, booking.Number);
        Assert.Equal(SeatHoldStatus.Converted, (await db.SeatHolds.SingleAsync()).Status);
        Assert.Empty(await db.Payments.ToArrayAsync());
    }

    [PostgresTheory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"holdId\":null}")]
    [InlineData("{\"holdId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"holdId\":\"invalid\"}")]
    [InlineData("{")]
    public async Task CustomerBooking_InvalidBodyRejectsWithoutMutatingHold(string body)
    {
        var setup = await OwnedHold();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/bookings");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", setup.Owner);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        var problem = await AssertProblem(response, 400, "validation_failed");
        if (body is "{}" or "{\"holdId\":\"00000000-0000-0000-0000-000000000000\"}")
            Assert.True(problem.GetProperty("errors").TryGetProperty("holdId", out _));
        await AssertOriginalHoldStillAllocated(setup.Hold);
    }

    [PostgresFact]
    public async Task CustomerBooking_RequiresAuthenticationAndCustomerPolicy()
    {
        var setup = await OwnedHold();
        using var anonymous = await client.PostAsJsonAsync("/api/bookings", new CreateBookingRequest(setup.Hold.Id));
        await AssertProblem(anonymous, 401, "unauthorized");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var owner = (await users.FindByIdAsync(setup.OwnerId.ToString()))!;
            Assert.True((await users.RemoveFromRoleAsync(owner, "Customer")).Succeeded);
        }
        using var login = await Login("owner@example.test", Password);
        var token = (await login.Content.ReadFromJsonAsync<TicketBooking.Contracts.Auth.LoginResponse>())!.AccessToken;
        using var forbidden = await ConvertHold(setup.Hold.Id, token);
        await AssertProblem(forbidden, 403, "forbidden");
        await AssertOriginalHoldStillAllocated(setup.Hold);
    }

    [PostgresFact]
    public async Task CustomerBooking_RejectsAnotherCustomerEvenWithSubmittedOwnerId()
    {
        var setup = await OwnedHold();
        using var registered = await Register("other@example.test", "Other");
        using var login = await Login("other@example.test", Password);
        var token = (await login.Content.ReadFromJsonAsync<TicketBooking.Contracts.Auth.LoginResponse>())!.AccessToken;
        using var response = await AdminRequest(HttpMethod.Post, "/api/bookings", token,
            new { holdId = setup.Hold.Id, userId = setup.OwnerId });
        await AssertProblem(response, 403, "resource_not_owned");
        await AssertOriginalHoldStillAllocated(setup.Hold);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().Bookings.ToArrayAsync());
    }

    [PostgresTheory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task CustomerBooking_EnforcesHoldExpiryWithoutWaitingForWorker(int ticks)
    {
        var setup = await OwnedHold();
        clock.Set(setup.Hold.ExpiresAt.AddTicks(ticks));
        using var response = await ConvertHold(setup.Hold.Id, setup.Owner);
        if (ticks < 0)
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        else
        {
            await AssertProblem(response, 409, "hold_expired");
            await AssertOriginalHoldStillAllocated(setup.Hold);
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            Assert.Empty(await db.Bookings.ToArrayAsync());
            Assert.Empty(await db.BookingItems.ToArrayAsync());
        }
    }

    [PostgresFact]
    public async Task CustomerBooking_OpenApiDocumentsRequestResponseAndStableErrors()
    {
        using var response = await client.GetAsync("/openapi/v1.json");
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        var operation = document.GetProperty("paths").GetProperty("/api/bookings").GetProperty("post");
        Assert.True(operation.TryGetProperty("requestBody", out _));
        var responses = operation.GetProperty("responses");
        foreach (var status in new[] { "201", "400", "401", "403", "404", "409", "415", "500" })
            Assert.True(responses.TryGetProperty(status, out _), status);
        Assert.Contains("hold_expired", responses.GetProperty("409").GetProperty("description").GetString());
        Assert.Contains("resource_not_owned", responses.GetProperty("403").GetProperty("description").GetString());
    }
}
