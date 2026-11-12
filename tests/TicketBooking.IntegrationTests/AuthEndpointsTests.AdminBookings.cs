using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Administration;
using TicketBooking.Contracts.Auth;
using TicketBooking.Contracts.Booking;
using TicketBooking.Domain.Booking;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Identity;
using TicketBooking.Infrastructure.Identity;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresFact]
    public async Task AdminBookings_RejectsAnonymousInvalidTokensAndCustomersEvenWhenTheyOwnTheBooking()
    {
        var setup = await OwnedHold();
        var booking = await CreateBookingForRead(setup.Hold.Id, setup.Owner);
        using var anonymous = await client.GetAsync("/api/admin/bookings");
        await AssertProblem(anonymous, 401, "unauthorized");
        using var invalid = await AdminRequest(HttpMethod.Get, "/api/admin/bookings", "invalid-token");
        await AssertProblem(invalid, 401, "unauthorized");
        using var customer = await AdminRequest(HttpMethod.Get,
            $"/api/admin/bookings?role=Admin&userId={setup.OwnerId}&page=0", setup.Owner);
        await AssertProblem(customer, 403, "forbidden");
        await AssertBookingAllocation(booking, BookingStatus.PendingPayment, SessionSeatStatus.Held, 2);
    }

    [PostgresFact]
    public async Task AdminBookings_FreshTokenWithoutAdminRoleIsForbidden()
    {
        await AdminToken();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = (await users.FindByEmailAsync("alice@example.test"))!;
            Assert.True((await users.RemoveFromRoleAsync(admin, RoleNames.Admin)).Succeeded);
        }
        var token = (await LoginToken()).AccessToken;
        using var response = await AdminRequest(HttpMethod.Get, "/api/admin/bookings", token);
        await AssertProblem(response, 403, "forbidden");
    }

    [PostgresFact]
    public async Task AdminBookings_ReturnsAllOwnersAndStatusesWithBoundedStablePagination()
    {
        clock.Set(new DateTimeOffset(clock.GetUtcNow().UtcTicks / 10 * 10, TimeSpan.Zero));
        var setup = await OwnedHold();
        var original = await CreateBookingForRead(setup.Hold.Id, setup.Owner);
        using var registered = await Register("other@example.test", "Other");
        var other = (await registered.Content.ReadFromJsonAsync<UserResponse>())!;
        var expected = new List<(Guid Id, Guid OwnerId, DateTimeOffset CreatedAt)> { (original.Id, setup.OwnerId, original.CreatedAt) };
        for (var index = 0; index < 4; index++)
        {
            var at = original.CreatedAt.AddMinutes(index < 2 ? 1 : 2);
            var ownerId = index % 2 == 0 ? other.Id : setup.OwnerId;
            expected.Add((await SeedBookingReadHistory(ownerId, at, (BookingStatus)(index + 1)), ownerId, at));
        }
        var ordered = expected.OrderByDescending(value => value.CreatedAt).ThenByDescending(value => value.Id).ToArray();
        var seen = new List<Guid>();
        var statuses = new HashSet<int>();
        for (var page = 1; page <= 3; page++)
        {
            using var response = await AdminRequest(HttpMethod.Get,
                $"/api/admin/bookings?page={page}&pageSize=2", setup.Admin);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<AdminBookingListResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(page, result.Page);
            Assert.Equal(2, result.PageSize);
            Assert.Equal(5, result.TotalCount);
            Assert.Equal(3, result.TotalPages);
            Assert.Equal(page > 1, result.HasPreviousPage);
            Assert.Equal(page < 3, result.HasNextPage);
            foreach (var item in result.Items)
            {
                var stored = Assert.Single(expected, value => value.Id == item.Id);
                Assert.Equal(stored.OwnerId, item.UserId);
                Assert.Equal(stored.CreatedAt, item.CreatedAt);
                Assert.Equal(original.SessionId, item.SessionId);
                Assert.Equal(item.Id == original.Id ? original.TotalAmount : 37.25m, item.TotalAmount);
                Assert.Equal(item.Id == original.Id ? original.Currency : "EUR", item.Currency);
                seen.Add(item.Id);
                statuses.Add(item.Status);
            }
            foreach (var field in new[] { "sourceHoldId", "allocationVersion", "password", "email", "providerReference", "idempotencyKey" })
                Assert.DoesNotContain(field, json, StringComparison.OrdinalIgnoreCase);
            using var document = JsonDocument.Parse(json);
            Assert.All(document.RootElement.GetProperty("items").EnumerateArray(), item => Assert.False(item.TryGetProperty("items", out _)));
        }
        Assert.Equal(ordered.Select(value => value.Id), seen);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, statuses.Order());
        using var beyond = await AdminRequest(HttpMethod.Get, "/api/admin/bookings?page=4&pageSize=2", setup.Admin);
        var empty = (await beyond.Content.ReadFromJsonAsync<AdminBookingListResponse>())!;
        Assert.Empty(empty.Items);
        Assert.Equal(5, empty.TotalCount);
        // Authorization for this explicit management route grants no bypass on customer routes.
        using var details = await AdminRequest(HttpMethod.Get, $"/api/bookings/{original.Id}", setup.Admin);
        await AssertProblem(details, 403, "resource_not_owned");
        using var mine = await AdminRequest(HttpMethod.Get, "/api/me/bookings", setup.Admin);
        Assert.Equal(0, (await mine.Content.ReadFromJsonAsync<BookingListResponse>())!.TotalCount);
    }

    [PostgresTheory]
    [InlineData("page=0", "page")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("page=2147483647&pageSize=100", "page")]
    [InlineData("page=1&page=2", "page")]
    [InlineData("pageSize=1&pageSize=2", "pageSize")]
    public async Task AdminBookings_ValidatesPaginationWithStableFieldErrors(string query, string field)
    {
        var token = await AdminToken();
        using var response = await AdminRequest(HttpMethod.Get, "/api/admin/bookings?" + query, token);
        var problem = await AssertProblem(response, 400, "validation_failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [PostgresFact]
    public async Task AdminBookings_EmptyDefaultAndMaximumPagesUseSharedPaginationContract()
    {
        var token = await AdminToken();
        foreach (var pageSize in new[] { 20, 100 })
        {
            using var response = await AdminRequest(HttpMethod.Get,
                pageSize == 20 ? "/api/admin/bookings" : "/api/admin/bookings?pageSize=100", token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = (await response.Content.ReadFromJsonAsync<AdminBookingListResponse>())!;
            Assert.Empty(result.Items);
            Assert.Equal(0, result.TotalCount);
            Assert.Equal(1, result.Page);
            Assert.Equal(pageSize, result.PageSize);
            Assert.Equal(0, result.TotalPages);
            Assert.False(result.HasNextPage);
            Assert.False(result.HasPreviousPage);
        }
    }

    [PostgresFact]
    public async Task AdminBookings_CountAndPageShareASnapshotAcrossConcurrentInsertion()
    {
        clock.Set(new DateTimeOffset(clock.GetUtcNow().UtcTicks / 10 * 10, TimeSpan.Zero));
        var setup = await OwnedHold();
        var original = await CreateBookingForRead(setup.Hold.Id, setup.Owner);
        bookingHistoryReads.Enabled = true;
        var reading = AdminRequest(HttpMethod.Get, "/api/admin/bookings", setup.Admin);
        try
        {
            await bookingHistoryReads.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await SeedBookingReadHistory(Guid.NewGuid(), original.CreatedAt.AddMinutes(1), BookingStatus.PendingPayment);
        }
        finally { bookingHistoryReads.Continue.TrySetResult(); }
        using var response = await reading.WaitAsync(TimeSpan.FromSeconds(20));
        var page = (await response.Content.ReadFromJsonAsync<AdminBookingListResponse>())!;
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(original.Id, Assert.Single(page.Items).Id);
        using var later = await AdminRequest(HttpMethod.Get, "/api/admin/bookings", setup.Admin);
        Assert.Equal(2, (await later.Content.ReadFromJsonAsync<AdminBookingListResponse>())!.TotalCount);
    }

    [PostgresFact]
    public async Task AdminBookings_OpenApiDocumentsAdminResponsesAndPagination()
    {
        using var response = await client.GetAsync("/openapi/v1.json");
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        var operation = document.GetProperty("paths").GetProperty("/api/admin/bookings").GetProperty("get");
        foreach (var status in new[] { "200", "400", "401", "403", "500" })
            Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
        Assert.Equal(new[] { "page", "pageSize" }, operation.GetProperty("parameters").EnumerateArray()
            .Select(value => value.GetProperty("name").GetString()).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("resource_not_owned", operation.GetProperty("responses").GetProperty("403").GetProperty("description").GetString());
    }
}
