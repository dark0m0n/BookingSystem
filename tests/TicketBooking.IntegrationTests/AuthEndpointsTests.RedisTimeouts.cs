using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Catalogue;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Caching;
using TicketBooking.Infrastructure.Persistence;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresTheory]
    [InlineData("create")]
    [InlineData("hold-read")]
    [InlineData("seat-read")]
    [InlineData("cancel")]
    [InlineData("convert")]
    [InlineData("expire")]
    public async Task RedisTimeouts_NonCompletingCacheCannotBlockDatabaseWork(string operation)
    {
        app.Services.GetRequiredService<RedisCachePolicyOptions>().OperationTimeout = TimeSpan.FromMilliseconds(25);
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Set up data before blocking cache I/O; the test then simulates a provider ignoring cancellation.
        var setup = await OwnedHold();
        EnableSmallSeatCache();
        holdCacheStorage.BeforeGetAsync = holdCacheStorage.BeforeSetAsync = holdCacheStorage.BeforeRemoveAsync = _ => blocked.Task;
        try
        {
            if (operation == "create")
            {
                using var cancelled = await AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
                using var created = await AdminRequest(HttpMethod.Post, $"/api/sessions/{setup.Session.Id}/holds", setup.Owner,
                    new CreateHoldRequest(setup.Hold.Items.Select(item => item.SessionSeatId).ToArray()))
                    .WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            }
            else if (operation == "hold-read")
            {
                holdCacheStorage.Entries.Clear();
                using var read = await AdminRequest(HttpMethod.Get, $"/api/holds/{setup.Hold.Id}", setup.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(HttpStatusCode.OK, read.StatusCode);
                var hold = (await read.Content.ReadFromJsonAsync<HoldResponse>())!;
                Assert.Equal(setup.Hold.TotalAmount, hold.TotalAmount);
                Assert.Equal(setup.Hold.Items.ToArray(), hold.Items.ToArray());
            }
            else if (operation == "seat-read")
            {
                using var read = await client.GetAsync($"/api/sessions/{setup.Session.Id}/seats").WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(HttpStatusCode.OK, read.StatusCode);
                Assert.All((await read.Content.ReadFromJsonAsync<SessionSeatMapResponse>())!.Seats,
                    seat => Assert.Equal((int)SessionSeatStatus.Held, seat.Status));
            }
            else if (operation == "cancel")
            {
                using var cancelled = await AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
            }
            else if (operation == "convert")
            {
                using var converted = await ConvertHold(setup.Hold.Id, setup.Owner).WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(HttpStatusCode.OK, converted.StatusCode);
            }
            else
            {
                clock.Set(setup.Hold.ExpiresAt);
                Assert.Equal(1, (await ExpiryProcessor().ProcessBatchAsync().WaitAsync(TimeSpan.FromSeconds(5))).Expired);
            }
            Assert.False(blocked.Task.IsCompleted);
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var expected = operation switch
            {
                "create" or "cancel" => SeatHoldStatus.Cancelled,
                "convert" => SeatHoldStatus.Converted,
                "expire" => SeatHoldStatus.Expired,
                _ => SeatHoldStatus.Active
            };
            Assert.Equal(expected, (await db.SeatHolds.SingleAsync(hold => hold.Id == setup.Hold.Id)).Status);
            Assert.All(await db.SessionSeats.ToArrayAsync(), seat => Assert.Equal(
                operation is "cancel" or "expire" ? SessionSeatStatus.Available : SessionSeatStatus.Held, seat.Status));
            Assert.Equal(operation == "convert" ? 1 : 0, await db.Bookings.CountAsync());
        }
        finally { blocked.TrySetException(new IOException("Late Redis failure after database fallback.")); }
    }
}
