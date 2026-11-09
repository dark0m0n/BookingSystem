using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using TicketBooking.Application.Reservation;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Caching;

namespace TicketBooking.IntegrationTests;

public class RedisHoldCacheLiveTests
{
    [RedisFact]
    public async Task Redis_PersistsSnapshotsWithBoundedTtlAndSupportsMissRecoveryAndInvalidation()
    {
        var connectionString = Environment.GetEnvironmentVariable("TICKETBOOKING_TEST_REDIS_CONNECTION_STRING")!;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = connectionString
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddHoldCaching(configuration);
        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IHoldCache>();
        using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var redis = connection.GetDatabase();
        var clock = provider.GetRequiredService<TimeProvider>();
        var hold = new HoldResponse(Guid.NewGuid(), Guid.NewGuid(), clock.GetUtcNow().AddMinutes(10),
            (int)SeatHoldStatus.Active, 100m, "EUR", [new HoldItemResponse(Guid.NewGuid(), 100m, "EUR")]);
        var key = $"hold:{hold.Id:D}";
        try
        {
            // Establish the real provider connection before asserting cache contents; cold attempts may time out by design.
            await provider.GetRequiredService<IDistributedCache>().GetAsync(key).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(await cache.GetAsync(hold.Id));
            await cache.SetAsync(hold);
            var ttl = Assert.IsType<TimeSpan>(await redis.KeyTimeToLiveAsync(key));
            Assert.InRange(ttl, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(10));
            var snapshot = Assert.IsType<HoldResponse>(await cache.GetAsync(hold.Id));
            Assert.Equal(hold.ExpiresAt, snapshot.ExpiresAt);
            Assert.Equal(hold.Items.ToArray(), snapshot.Items.ToArray());
            Assert.True(await redis.KeyTimeToLiveAsync(key) <= ttl);

            // A refill must use the same absolute expiry, even if the key was evicted.
            await redis.KeyDeleteAsync(key);
            await cache.SetAsync(hold);
            // The provider rounds Redis TTL to whole seconds; compare the stored absolute deadline too.
            Assert.Equal(hold.ExpiresAt.Ticks, (long)await redis.HashGetAsync(key, "absexp"));
            Assert.InRange(Assert.IsType<TimeSpan>(await redis.KeyTimeToLiveAsync(key)), TimeSpan.Zero,
                hold.ExpiresAt - clock.GetUtcNow() + TimeSpan.FromSeconds(1));
            await redis.HashSetAsync(key, "data", "invalid JSON");
            Assert.Null(await cache.GetAsync(hold.Id));
            await cache.SetAsync(hold);
            Assert.NotNull(await cache.GetAsync(hold.Id));

            await redis.KeyExpireAsync(key, TimeSpan.Zero);
            Assert.Null(await cache.GetAsync(hold.Id));
            await cache.SetAsync(hold);
            await cache.RemoveAsync(hold.Id);
            Assert.False(await redis.KeyExistsAsync(key));
        }
        finally
        {
            await redis.KeyDeleteAsync(key);
        }
    }

    public sealed class RedisFactAttribute : FactAttribute
    {
        public RedisFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TICKETBOOKING_TEST_REDIS_CONNECTION_STRING")))
                Skip = "Set TICKETBOOKING_TEST_REDIS_CONNECTION_STRING to an isolated Redis instance.";
        }
    }
}
