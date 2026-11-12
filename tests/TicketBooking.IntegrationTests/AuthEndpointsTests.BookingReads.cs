using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Auth;
using TicketBooking.Contracts.Booking;
using TicketBooking.Domain;
using TicketBooking.Domain.Booking;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Identity;
using TicketBooking.Infrastructure.Persistence;
using BookingEntity = TicketBooking.Domain.Booking.Booking;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    private readonly BookingHistoryReadGate bookingHistoryReads = new();

    [PostgresTheory]
    [InlineData(BookingStatus.PendingPayment)]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Expired)]
    [InlineData(BookingStatus.Refunded)]
    public async Task BookingDetails_ReturnStoredHistoryAndSnapshotsWithoutMutatingState(BookingStatus status)
    {
        clock.Set(new DateTimeOffset(clock.GetUtcNow().UtcTicks / 10 * 10, TimeSpan.Zero));
        var setup = await OwnedHold();
        var original = await CreateBookingForRead(setup.Hold.Id, setup.Owner);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var booking = await db.Bookings.SingleAsync();
            // Historical fixtures include Refunded, whose domain transition is deferred.
            db.Entry(booking).Property(value => value.Status).CurrentValue = status;
            foreach (var seat in await db.SessionSeats.ToArrayAsync())
            {
                db.Entry(seat).ComplexProperty(value => value.Price).Property(value => value.Amount).CurrentValue = 75m;
                db.Entry(seat).ComplexProperty(value => value.Price).Property(value => value.Currency).CurrentValue = "EUR";
            }
            await db.SaveChangesAsync();
        }
        clock.Set(original.PaymentDeadline.AddMinutes(1));
        using var login = await Login("owner@example.test", Password);
        var owner = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        using var response = await AdminRequest(HttpMethod.Get, $"/api/bookings/{original.Id}", owner);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BookingResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(original with { Status = (int)status, Items = result.Items }, result);
        Assert.Equal(original.Items.OrderBy(value => value.SessionSeatId), result.Items);
        foreach (var field in new[] { "userId", "sourceHoldId", "allocationVersion" })
            Assert.DoesNotContain(field, json, StringComparison.OrdinalIgnoreCase);
        await AssertBookingAllocation(original, status, SessionSeatStatus.Held, 3);
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BookingDetails_RejectAnotherCustomerOrAdminWithoutOwnershipBypass(bool admin)
    {
        var setup = await OwnedHold();
        var booking = await CreateBookingForRead(setup.Hold.Id, setup.Owner);
        var token = setup.Admin;
        if (!admin)
        {
            using var registered = await Register("other@example.test", "Other");
            using var login = await Login("other@example.test", Password);
            token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        }
        using var response = await AdminRequest(HttpMethod.Get, $"/api/bookings/{booking.Id}?userId={setup.OwnerId}", token);
        await AssertProblem(response, 403, "resource_not_owned");
        Assert.True(response.Headers.CacheControl!.NoStore);
        await AssertBookingAllocation(booking, BookingStatus.PendingPayment, SessionSeatStatus.Held, 2);
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BookingReads_RequireAuthenticationAndCustomerPolicy(bool list)
    {
        var setup = await OwnedHold();
        var booking = await CreateBookingForRead(setup.Hold.Id, setup.Owner);
        var path = list ? "/api/me/bookings" : $"/api/bookings/{booking.Id}";
        using var anonymous = await client.GetAsync(path);
        await AssertProblem(anonymous, 401, "unauthorized");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.RemoveFromRoleAsync((await users.FindByIdAsync(setup.OwnerId.ToString()))!, "Customer")).Succeeded);
        }
        using var login = await Login("owner@example.test", Password);
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        using var forbidden = await AdminRequest(HttpMethod.Get, path, token);
        await AssertProblem(forbidden, 403, "forbidden");
        await AssertBookingAllocation(booking, BookingStatus.PendingPayment, SessionSeatStatus.Held, 2);
    }

    [PostgresFact]
    public async Task BookingDetails_MissingBookingReturnsNotFound()
    {
        var token = await AdminToken();
        using var response = await AdminRequest(HttpMethod.Get, $"/api/bookings/{Guid.NewGuid()}", token);
        await AssertProblem(response, 404, "not_found");
    }

    [PostgresFact]
    public async Task MyBookings_PaginatesOnlyTheCallerHistoryWithStableNewestFirstOrdering()
    {
        clock.Set(new DateTimeOffset(clock.GetUtcNow().UtcTicks / 10 * 10, TimeSpan.Zero));
        var setup = await OwnedHold();
        var initial = await CreateBookingForRead(setup.Hold.Id, setup.Owner);
        var expected = new List<(Guid Id, DateTimeOffset CreatedAt)> { (initial.Id, initial.CreatedAt) };
        for (var index = 0; index < 4; index++)
        {
            // Tied creation times exercise the Id tie-breaker across page boundaries.
            var at = initial.CreatedAt.AddMinutes(index < 2 ? 1 : 2);
            var id = await SeedBookingReadHistory(setup.OwnerId, at, (BookingStatus)(index + 1));
            expected.Add((id, at));
        }
        await SeedBookingReadHistory(Guid.NewGuid(), initial.CreatedAt.AddMinutes(3), BookingStatus.PendingPayment);
        var ordered = expected.OrderByDescending(value => value.CreatedAt).ThenByDescending(value => value.Id).Select(value => value.Id).ToArray();
        var seen = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            using var response = await AdminRequest(HttpMethod.Get,
                $"/api/me/bookings?page={page}&pageSize=2&userId={Guid.NewGuid()}", setup.Owner);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<BookingListResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(page, result.Page);
            Assert.Equal(2, result.PageSize);
            Assert.Equal(5, result.TotalCount);
            Assert.Equal(3, result.TotalPages);
            Assert.Equal(page > 1, result.HasPreviousPage);
            Assert.Equal(page < 3, result.HasNextPage);
            Assert.All(result.Items, item =>
            {
                Assert.Equal(item.Id == initial.Id ? initial.TotalAmount : 37.25m, item.TotalAmount);
                Assert.Equal(item.Id == initial.Id ? initial.Currency : "EUR", item.Currency);
            });
            seen.AddRange(result.Items.Select(value => value.Id));
            foreach (var field in new[] { "userId", "sourceHoldId", "allocationVersion" })
                Assert.DoesNotContain(field, json, StringComparison.OrdinalIgnoreCase);
            using var document = JsonDocument.Parse(json);
            Assert.All(document.RootElement.GetProperty("items").EnumerateArray(), item => Assert.False(item.TryGetProperty("items", out _)));
        }
        Assert.Equal(ordered, seen);
        using var beyond = await AdminRequest(HttpMethod.Get, "/api/me/bookings?page=4&pageSize=2", setup.Owner);
        var empty = (await beyond.Content.ReadFromJsonAsync<BookingListResponse>())!;
        Assert.Empty(empty.Items);
        Assert.Equal(5, empty.TotalCount);
        using var admin = await AdminRequest(HttpMethod.Get, $"/api/me/bookings?userId={setup.OwnerId}", setup.Admin);
        var adminPage = (await admin.Content.ReadFromJsonAsync<BookingListResponse>())!;
        Assert.Empty(adminPage.Items);
        Assert.Equal(0, adminPage.TotalCount);
        Assert.Equal(1, adminPage.Page);
        Assert.Equal(20, adminPage.PageSize);
        Assert.Equal(0, adminPage.TotalPages);
    }

    [PostgresTheory]
    [InlineData("pageSize=100", 1, 100)]
    [InlineData("page=2147483647&pageSize=1", int.MaxValue, 1)]
    public async Task MyBookings_AcceptsMaximumPageSizeAndSupportedLargeOffsets(string query, int page, int pageSize)
    {
        var token = await AdminToken();
        using var response = await AdminRequest(HttpMethod.Get, "/api/me/bookings?" + query, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<BookingListResponse>())!;
        Assert.Equal(page, result.Page);
        Assert.Equal(pageSize, result.PageSize);
        Assert.Empty(result.Items);
    }

    [PostgresTheory]
    [InlineData("page=0", "page")]
    [InlineData("page=-1", "page")]
    [InlineData("page=", "page")]
    [InlineData("page=1.5", "page")]
    [InlineData("page=2147483648", "page")]
    [InlineData("page=2147483647&pageSize=100", "page")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("pageSize=", "pageSize")]
    [InlineData("pageSize=word", "pageSize")]
    [InlineData("page=1&page=2", "page")]
    [InlineData("pageSize=1&pageSize=2", "pageSize")]
    public async Task MyBookings_RejectsInvalidRepeatedAndOverflowingPagination(string query, string field)
    {
        var token = await AdminToken();
        using var response = await AdminRequest(HttpMethod.Get, "/api/me/bookings?" + query, token);
        var problem = await AssertProblem(response, 400, "validation_failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    [PostgresFact]
    public async Task MyBookings_CountAndPageUseOneSnapshotDuringConcurrentInsertion()
    {
        clock.Set(new DateTimeOffset(clock.GetUtcNow().UtcTicks / 10 * 10, TimeSpan.Zero));
        var setup = await OwnedHold();
        var original = await CreateBookingForRead(setup.Hold.Id, setup.Owner);
        bookingHistoryReads.Enabled = true;
        var reading = AdminRequest(HttpMethod.Get, "/api/me/bookings", setup.Owner);
        try
        {
            await bookingHistoryReads.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await SeedBookingReadHistory(setup.OwnerId, original.CreatedAt.AddMinutes(1), BookingStatus.PendingPayment);
        }
        finally { bookingHistoryReads.Continue.TrySetResult(); }
        using var response = await reading.WaitAsync(TimeSpan.FromSeconds(20));
        var page = (await response.Content.ReadFromJsonAsync<BookingListResponse>())!;
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(original.Id, Assert.Single(page.Items).Id);
        using var later = await AdminRequest(HttpMethod.Get, "/api/me/bookings", setup.Owner);
        Assert.Equal(2, (await later.Content.ReadFromJsonAsync<BookingListResponse>())!.TotalCount);
    }

    [PostgresFact]
    public async Task BookingReads_OpenApiDocumentsResponsesProblemsAndPagination()
    {
        using var response = await client.GetAsync("/openapi/v1.json");
        var paths = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paths");
        var details = paths.GetProperty("/api/bookings/{bookingId}").GetProperty("get");
        var list = paths.GetProperty("/api/me/bookings").GetProperty("get");
        foreach (var status in new[] { "200", "401", "403", "500" })
        {
            Assert.True(details.GetProperty("responses").TryGetProperty(status, out _));
            Assert.True(list.GetProperty("responses").TryGetProperty(status, out _));
        }
        Assert.True(details.GetProperty("responses").TryGetProperty("404", out _));
        Assert.True(list.GetProperty("responses").TryGetProperty("400", out _));
        Assert.Contains("resource_not_owned", details.GetProperty("responses").GetProperty("403").GetProperty("description").GetString());
        Assert.Equal(new[] { "page", "pageSize" }, list.GetProperty("parameters").EnumerateArray()
            .Select(value => value.GetProperty("name").GetString()).Order(StringComparer.Ordinal));
    }

    private async Task<BookingResponse> CreateBookingForRead(Guid holdId, string token)
    {
        using var created = await ConvertHold(holdId, token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var booking = (await created.Content.ReadFromJsonAsync<BookingResponse>())!;
        Assert.Equal($"/api/bookings/{booking.Id}", created.Headers.Location!.OriginalString);
        return booking;
    }

    private async Task<Guid> SeedBookingReadHistory(Guid ownerId, DateTimeOffset at, BookingStatus status)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var session = await db.Sessions.SingleAsync();
        var inventory = await db.SessionSeats.AsNoTracking().FirstAsync();
        // Historical fixtures use a detached available inventory snapshot; they never claim live seats.
        var source = new SessionSeat(inventory.Id, session.Id, inventory.SeatId, new Money(37.25m, "EUR"));
        var hold = new SeatHold(Guid.NewGuid(), session.Id, ownerId, session.StartsAt, [source], clock,
            scope.ServiceProvider.GetRequiredService<BookingTimingPolicy>());
        var booking = BookingEntity.CreateFromHold(Guid.NewGuid(), hold, ownerId, "BK-HISTORY-" + Guid.NewGuid().ToString("N"),
            clock, session.StartsAt, scope.ServiceProvider.GetRequiredService<BookingTimingPolicy>());
        hold.Convert(ownerId, clock);
        db.AddRange(hold, booking);
        db.Entry(booking).Property(value => value.CreatedAt).CurrentValue = at;
        db.Entry(booking).Property(value => value.Status).CurrentValue = status;
        await db.SaveChangesAsync();
        return booking.Id;
    }

    private sealed class BookingHistoryReadGate : DbCommandInterceptor
    {
        private int reads;
        public bool Enabled { get; set; }
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("count(", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("\"Bookings\"", StringComparison.Ordinal) && Interlocked.Increment(ref reads) == 1)
            {
                Reached.TrySetResult();
                await Continue.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
