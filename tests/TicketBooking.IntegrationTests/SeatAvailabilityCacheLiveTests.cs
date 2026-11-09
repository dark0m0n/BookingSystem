using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using TicketBooking.Application.Catalogue;
using TicketBooking.Contracts.Catalogue;
using TicketBooking.Infrastructure.Caching;
using RedisFact = TicketBooking.IntegrationTests.RedisHoldCacheLiveTests.RedisFactAttribute;

namespace TicketBooking.IntegrationTests;

public class SeatAvailabilityCacheLiveTests
{
    [RedisFact]
    public async Task RedisSeatMap_UsesBoundedTtlAndOnlyMatchesItsInventoryRevision()
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
        services.AddSeatAvailabilityCaching(configuration);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var cache = provider.GetRequiredService<ISeatAvailabilityCache>();
        var map = new SessionSeatMapResponse(Guid.NewGuid(), Guid.NewGuid(), Enumerable.Range(1, 64)
            .Select(number => new SeatMapSeatResponse(Guid.NewGuid(), Guid.NewGuid(), "A", number, 0, 100m, "UAH", 0, 0)).ToArray());
        var key = new SeatAvailabilityCacheKey(map.SessionId, map.AuditoriumId,
            SeatAvailabilityRevision.Create(map.Seats.Select(seat => new SeatAvailabilityVersion(seat.Id, seat.Version))));
        var redisKey = $"seat-availability:v1:{key.SessionId:D}:{key.AuditoriumId:D}:{key.Revision}";
        using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var redis = connection.GetDatabase();
        try
        {
            await provider.GetRequiredService<IDistributedCache>().GetAsync(redisKey).WaitAsync(TimeSpan.FromSeconds(5));
            await cache.SetAsync(key, map);
            var ttl = Assert.IsType<TimeSpan>(await redis.KeyTimeToLiveAsync(redisKey));
            Assert.InRange(ttl, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(5));
            Assert.Equal(map.Seats.ToArray(), (await cache.GetAsync(key))!.Seats.ToArray());
            Assert.True(await redis.KeyTimeToLiveAsync(redisKey) <= ttl);
            var changed = key with
            {
                Revision = SeatAvailabilityRevision.Create(map.Seats
                .Select(seat => new SeatAvailabilityVersion(seat.Id, seat.Version + 1)))
            };
            Assert.Null(await cache.GetAsync(changed));
            Assert.True(await redis.KeyExistsAsync(redisKey));
            await redis.KeyExpireAsync(redisKey, TimeSpan.Zero);
            Assert.Null(await cache.GetAsync(key));
        }
        finally { await redis.KeyDeleteAsync(redisKey); }
    }
}
