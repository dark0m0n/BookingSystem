using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Api.Reservation;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CacheAuthority_MissingRedisDataCannotReleaseUnexpiredSeats(bool unavailable)
    {
        holdCacheStorage.FailReads = holdCacheStorage.FailWrites = holdCacheStorage.FailRemovals = unavailable;
        var setup = await OwnedHold();
        holdCacheStorage.Entries.Clear(); // Eviction/early TTL expiry is not a lifecycle event.
        clock.Set(setup.Hold.ExpiresAt.AddTicks(-1));
        using var read = await AdminRequest(HttpMethod.Get, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal((int)SeatHoldStatus.Active, (await read.Content.ReadFromJsonAsync<HoldResponse>())!.Status);
        using var competing = await AdminRequest(HttpMethod.Post, $"/api/sessions/{setup.Session.Id}/holds", setup.Owner,
            new CreateHoldRequest(setup.Hold.Items.Select(item => item.SessionSeatId).ToArray()));
        await AssertProblem(competing, 409, "seat_not_available");
        Assert.Equal(0, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        await AssertOriginalHoldStillAllocated(setup.Hold);
    }

    [PostgresTheory]
    [InlineData("missing", 0)]
    [InlineData("missing", 1)]
    [InlineData("retained", 0)]
    [InlineData("retained", 1)]
    [InlineData("unavailable", 0)]
    [InlineData("unavailable", 1)]
    public async Task CacheAuthority_DeadlineRejectsUseAndDatabasePollingReleasesWithoutNotifications(string cacheState, int ticks)
    {
        var setup = await OwnedHold();
        EnableSmallSeatCache();
        await SeatCacheMap(setup.Session.Id);
        if (cacheState == "missing") holdCacheStorage.Entries.Clear();
        if (cacheState == "unavailable")
            holdCacheStorage.FailReads = holdCacheStorage.FailWrites = holdCacheStorage.FailRemovals = true;
        clock.Set(setup.Hold.ExpiresAt.AddTicks(ticks));

        // No notification is delivered and no worker pass has run. Stored deadline checks still reject use.
        using var conversion = await ConvertHold(setup.Hold.Id, setup.Owner);
        await AssertProblem(conversion, 409, "hold_expired");
        using var cancellation = await AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        await AssertProblem(cancellation, 409, "hold_expired");
        using var competing = await AdminRequest(HttpMethod.Post, $"/api/sessions/{setup.Session.Id}/holds", setup.Owner,
            new CreateHoldRequest(setup.Hold.Items.Select(item => item.SessionSeatId).ToArray()));
        await AssertProblem(competing, 409, "seat_not_available");
        Assert.All((await SeatCacheMap(setup.Session.Id)).Seats, seat => Assert.Equal((int)SessionSeatStatus.Held, seat.Status));
        await AssertOriginalHoldStillAllocated(setup.Hold);

        Assert.Equal(1, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        Assert.Equal(0, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        using var history = await AdminRequest(HttpMethod.Get, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        Assert.Equal((int)SeatHoldStatus.Expired, (await history.Content.ReadFromJsonAsync<HoldResponse>())!.Status);
        Assert.All((await SeatCacheMap(setup.Session.Id)).Seats, seat =>
        {
            Assert.Equal((int)SessionSeatStatus.Available, seat.Status);
            Assert.Equal(2, seat.Version);
        });
        using var acquired = await AdminRequest(HttpMethod.Post, $"/api/sessions/{setup.Session.Id}/holds", setup.Owner,
            new CreateHoldRequest(setup.Hold.Items.Select(item => item.SessionSeatId).ToArray()));
        Assert.Equal(HttpStatusCode.Created, acquired.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Empty(await db.Bookings.ToArrayAsync());
        Assert.All(await db.SessionSeats.ToArrayAsync(), seat => Assert.Equal(3, seat.Version));
    }

    [PostgresFact]
    public async Task CacheAuthority_HostedWorkerStartsAndReleasesWithRedisUnavailable()
    {
        holdCacheStorage.FailReads = holdCacheStorage.FailWrites = holdCacheStorage.FailRemovals = true;
        var setup = await OwnedHold();
        clock.Set(setup.Hold.ExpiresAt);
        var log = new ExpiryBatchLog();
        using var worker = new HoldExpiryWorker(ExpiryProcessor(), new HoldExpiryOptions { PollInterval = TimeSpan.FromHours(1) }, clock, log);
        await worker.StartAsync(CancellationToken.None);
        try { await log.Completed.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await worker.StopAsync(stop.Token);
        }
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Equal(SeatHoldStatus.Expired, (await db.SeatHolds.SingleAsync()).Status);
        Assert.All(await db.SessionSeats.ToArrayAsync(), seat =>
        {
            Assert.Equal(SessionSeatStatus.Available, seat.Status);
            Assert.Equal(2, seat.Version);
        });
        Assert.True(holdCacheStorage.Removals > 0);
    }
}
