using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TicketBooking.Application.Reservation;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Caching;

namespace TicketBooking.IntegrationTests;

public class RedisHoldCacheTests
{
    private readonly ManualTimeProvider clock = new();
    private readonly RecordingDistributedCache storage = new();
    private RedisHoldCache Cache => new(storage, clock, NullLogger<RedisHoldCache>.Instance,
        new RedisCacheTimeoutPolicy(new RedisCachePolicyOptions(), clock));
    private HoldResponse ActiveHold => new(Guid.NewGuid(), Guid.NewGuid(), clock.GetUtcNow().AddMinutes(10),
        (int)SeatHoldStatus.Active, 123.45m, "UAH", [new HoldItemResponse(Guid.NewGuid(), 123.45m, "UAH")]);

    [Fact]
    public async Task CacheRoundTrip_PreservesSnapshotsAndDoesNotRenewExpiryOnRead()
    {
        var hold = ActiveHold;
        clock.Advance(TimeSpan.FromMinutes(3));
        await Cache.SetAsync(hold);
        var entry = Assert.Single(storage.Entries);
        Assert.Equal($"hold:{hold.Id:D}", entry.Key);
        Assert.Equal(TimeSpan.FromMinutes(7), entry.Value.Options.AbsoluteExpiration - clock.GetUtcNow());
        Assert.Null(entry.Value.Options.AbsoluteExpirationRelativeToNow);
        Assert.Equal(hold.ExpiresAt, entry.Value.Options.AbsoluteExpiration);
        Assert.Null(entry.Value.Options.SlidingExpiration);
        clock.Advance(TimeSpan.FromMinutes(2));
        var cached = Assert.IsType<HoldResponse>(await Cache.GetAsync(hold.Id));
        Assert.Equal(hold.Id, cached.Id);
        Assert.Equal(hold.SessionId, cached.SessionId);
        Assert.Equal(hold.ExpiresAt, cached.ExpiresAt);
        Assert.Equal(hold.TotalAmount, cached.TotalAmount);
        Assert.Equal(hold.Currency, cached.Currency);
        Assert.Equal(hold.Items.ToArray(), cached.Items.ToArray());
        Assert.Equal(1, storage.Writes);
        await Cache.SetAsync(hold);
        Assert.Equal(TimeSpan.FromMinutes(5), storage.Entries[entry.Key].Options.AbsoluteExpiration - clock.GetUtcNow());
        Assert.Equal(hold.ExpiresAt, storage.Entries[entry.Key].Options.AbsoluteExpiration);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ExpiryBoundary_NeverCachesOrReturnsAnElapsedHold(int ticksFromExpiry)
    {
        var hold = ActiveHold;
        await Cache.SetAsync(hold);
        clock.Advance(TimeSpan.FromMinutes(10).Add(TimeSpan.FromTicks(ticksFromExpiry)));
        if (ticksFromExpiry < 0)
        {
            Assert.NotNull(await Cache.GetAsync(hold.Id));
            await Cache.SetAsync(hold);
            Assert.Equal(TimeSpan.FromTicks(1), Assert.Single(storage.Entries).Value.Options.AbsoluteExpiration - clock.GetUtcNow());
        }
        else
        {
            Assert.Null(await Cache.GetAsync(hold.Id));
            await Cache.SetAsync(hold);
            Assert.Empty(storage.Entries);
            Assert.Equal(1, storage.Writes);
        }
    }

    [Theory]
    [InlineData(SeatHoldStatus.Cancelled)]
    [InlineData(SeatHoldStatus.Converted)]
    [InlineData(SeatHoldStatus.Expired)]
    public async Task TerminalState_RemovesExistingCacheEntry(SeatHoldStatus status)
    {
        var hold = ActiveHold;
        await Cache.SetAsync(hold);
        await Cache.SetAsync(hold with { Status = (int)status });
        Assert.Empty(storage.Entries);
        Assert.Equal(1, storage.Writes);
    }

    [Theory]
    [InlineData("invalid json")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task CorruptPayload_IsACacheMiss(string json)
    {
        var id = Guid.NewGuid();
        storage.Set($"hold:{id:D}", Encoding.UTF8.GetBytes(json), new());
        Assert.Null(await Cache.GetAsync(id));
    }

    [Fact]
    public async Task InvalidPayloadLookup_DoesNotRemoveBeforeTheCallersCommit()
    {
        var hold = ActiveHold;
        await Cache.SetAsync(hold);
        clock.Advance(TimeSpan.FromMinutes(10));
        var original = Assert.Single(storage.Entries);
        Assert.Null(await Cache.GetAsync(hold.Id));
        Assert.Equal(0, storage.Removals);
        Assert.Equal(1, storage.Writes);
        Assert.Same(original.Value, storage.Entries[original.Key]);
    }

    [Fact]
    public async Task WrongHoldPayload_IsACacheMiss()
    {
        var id = Guid.NewGuid();
        storage.Set($"hold:{id:D}", JsonSerializer.SerializeToUtf8Bytes(ActiveHold), new());
        Assert.Null(await Cache.GetAsync(id));
    }

    [Fact]
    public async Task RedisUnavailable_ReadWriteAndInvalidationRemainBestEffort()
    {
        storage.FailReads = storage.FailWrites = storage.FailRemovals = true;
        var hold = ActiveHold;
        Assert.Null(await Cache.GetAsync(hold.Id));
        await Cache.SetAsync(hold);
        await Cache.RemoveAsync(hold.Id);
    }

    [Fact]
    public async Task ExpirationDuringConnection_SkipsWriteAndRemovesStaleEntry()
    {
        var hold = ActiveHold;
        await Cache.SetAsync(hold);
        storage.ExpireDuringWrite = true;
        await Cache.SetAsync(hold);
        Assert.Empty(storage.Entries);
    }

    [Fact]
    public async Task RequestCancellation_IsNotSwallowedAsACacheMiss()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Cache.GetAsync(Guid.NewGuid(), cancellation.Token));
    }

    [Fact]
    public void Registration_UsesConfiguredRedisWithoutConnectingDuringStartup()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = "localhost:16379,abortConnect=false"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddHoldCaching(configuration);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.IsType<RedisHoldCache>(provider.GetRequiredService<IHoldCache>());
        Assert.Equal("localhost:16379,abortConnect=false", provider.GetRequiredService<IOptions<RedisCacheOptions>>().Value.Configuration);
        Assert.IsAssignableFrom<RedisCache>(provider.GetRequiredService<IDistributedCache>());
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
    }
}
