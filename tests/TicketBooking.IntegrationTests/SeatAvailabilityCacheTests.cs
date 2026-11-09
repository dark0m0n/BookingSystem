using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TicketBooking.Application.Catalogue;
using TicketBooking.Contracts.Catalogue;
using TicketBooking.Infrastructure.Caching;

namespace TicketBooking.IntegrationTests;

public class SeatAvailabilityCacheTests
{
    private readonly ManualTimeProvider clock = new();
    private readonly RecordingDistributedCache storage = new();
    private readonly SeatAvailabilityCacheOptions options = new() { MinimumSeatCount = 2 };
    private RedisSeatAvailabilityCache Cache => new(storage, clock, options, NullLogger<RedisSeatAvailabilityCache>.Instance,
        new RedisCacheTimeoutPolicy(new RedisCachePolicyOptions(), clock));
    private static SessionSeatMapResponse Map => new(Guid.NewGuid(), Guid.NewGuid(),
    [
        new(Guid.NewGuid(), Guid.NewGuid(), "A", 1, 0, 100m, "UAH", 0, 0),
        new(Guid.NewGuid(), Guid.NewGuid(), "A", 2, 0, 150m, "UAH", 1, 1)
    ]);

    private static SeatAvailabilityCacheKey Key(SessionSeatMapResponse map) => new(map.SessionId, map.AuditoriumId,
        SeatAvailabilityRevision.Create(map.Seats.Select(seat => new SeatAvailabilityVersion(seat.Id, seat.Version))));

    [Fact]
    public async Task RoundTrip_PreservesMapAndUsesShortNonSlidingExpiry()
    {
        var map = Map;
        var key = Key(map);
        await Cache.SetAsync(key, map);
        var entry = Assert.Single(storage.Entries);
        Assert.StartsWith($"seat-availability:v1:{map.SessionId:D}:{map.AuditoriumId:D}:", entry.Key);
        Assert.Equal(clock.GetUtcNow().AddSeconds(5), entry.Value.Options.AbsoluteExpiration);
        Assert.Null(entry.Value.Options.SlidingExpiration);
        Assert.Null(entry.Value.Options.AbsoluteExpirationRelativeToNow);
        var cached = Assert.IsType<SessionSeatMapResponse>(await Cache.GetAsync(key));
        Assert.Equal(map.Seats.ToArray(), cached.Seats.ToArray());
        Assert.Equal(1, storage.Writes);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task ExpirationBoundary_RejectsElapsedPayloadEvenIfRedisRetainsIt(int ticks, bool hit)
    {
        var map = Map;
        await Cache.SetAsync(Key(map), map);
        clock.Advance(TimeSpan.FromSeconds(5).Add(TimeSpan.FromTicks(ticks)));
        Assert.Equal(hit, await Cache.GetAsync(Key(map)) is not null);
    }

    [Fact]
    public async Task SmallOrDisabledMaps_AreNotWritten()
    {
        Assert.False(Cache.IsUseful(1));
        Assert.True(Cache.IsUseful(2));
        var map = Map;
        await Cache.SetAsync(Key(map), map with { Seats = map.Seats.Take(1).ToArray() });
        options.Enabled = false;
        Assert.False(Cache.IsUseful(100));
        await Cache.SetAsync(Key(map), map);
        Assert.Empty(storage.Entries);
    }

    [Theory]
    [InlineData("increment")]
    [InlineData("replace")]
    [InlineData("remove")]
    public async Task InventoryChanges_SelectDifferentKeysEvenWhenTheOldFillArrivesLate(string change)
    {
        var map = Map;
        var changed = map.Seats.ToArray();
        changed[0] = change == "replace" ? changed[0] with { Id = Guid.NewGuid() } : changed[0] with { Version = 1 };
        var next = map with { Seats = change == "remove" ? map.Seats.Take(1).ToArray() : changed };
        Assert.NotEqual(Key(map), Key(next));
        await Cache.SetAsync(Key(map), map);
        Assert.Null(await Cache.GetAsync(Key(next)));
    }

    [Fact]
    public void Revision_IsOrderIndependentAndDoesNotOverflowAtMaximumVersion()
    {
        var rows = new[] { new SeatAvailabilityVersion(Guid.NewGuid(), long.MaxValue), new SeatAvailabilityVersion(Guid.NewGuid(), 0) };
        Assert.Equal(SeatAvailabilityRevision.Create(rows), SeatAvailabilityRevision.Create(rows.Reverse()));
        Assert.NotEqual(SeatAvailabilityRevision.Create(rows), SeatAvailabilityRevision.Create(rows.Select(row => row with { Version = 0 })));
    }

    [Fact]
    public async Task PayloadForAnotherRevision_IsACacheMiss()
    {
        var map = Map;
        await Cache.SetAsync(Key(map), map);
        var other = map with { Seats = map.Seats.Select(seat => seat with { Version = seat.Version + 1 }).ToArray() };
        await Cache.SetAsync(Key(other), other);
        var first = storage.Entries.Single(entry => entry.Key.EndsWith(Key(map).Revision, StringComparison.Ordinal));
        var second = storage.Entries.Single(entry => entry.Key.EndsWith(Key(other).Revision, StringComparison.Ordinal));
        storage.Entries[first.Key] = second.Value;
        Assert.Null(await Cache.GetAsync(Key(map)));
    }

    [Theory]
    [InlineData("invalid JSON")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task CorruptPayload_FallsBack(string json)
    {
        var map = Map;
        await Cache.SetAsync(Key(map), map);
        var entry = Assert.Single(storage.Entries);
        storage.Entries[entry.Key] = entry.Value with { Bytes = Encoding.UTF8.GetBytes(json) };
        Assert.Null(await Cache.GetAsync(Key(map)));
    }

    [Fact]
    public async Task RedisFailures_AreBestEffortButRequestCancellationPropagates()
    {
        var map = Map;
        storage.FailReads = storage.FailWrites = true;
        Assert.Null(await Cache.GetAsync(Key(map)));
        await Cache.SetAsync(Key(map), map);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Cache.GetAsync(Key(map), cancellation.Token));
    }

    [Theory]
    [InlineData("MinimumSeatCount", "0")]
    [InlineData("Lifetime", "00:00:00")]
    [InlineData("Lifetime", "00:00:31")]
    public void Configuration_RejectsInvalidBounds(string setting, string value)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"SeatAvailabilityCache:{setting}"] = value
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddSeatAvailabilityCaching(config));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan delta) => now += delta;
    }
}
