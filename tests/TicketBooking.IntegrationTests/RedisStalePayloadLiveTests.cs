using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using TicketBooking.Application.Catalogue;
using TicketBooking.Application.Reservation;
using TicketBooking.Contracts.Catalogue;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Infrastructure.Caching;
using RedisFact = TicketBooking.IntegrationTests.RedisHoldCacheLiveTests.RedisFactAttribute;

namespace TicketBooking.IntegrationTests;

public class RedisStalePayloadLiveTests
{
    [RedisFact]
    public async Task LiveKeysCannotOverrideExpiredPayloadsOrMismatchedInventoryRevisions()
    {
        var connectionString = Environment.GetEnvironmentVariable("TICKETBOOKING_TEST_REDIS_CONNECTION_STRING")!;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = connectionString,
            ["SeatAvailabilityCache:MinimumSeatCount"] = "1"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddHoldCaching(configuration);
        services.AddSeatAvailabilityCaching(configuration);
        using var provider = services.BuildServiceProvider();
        using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var redis = connection.GetDatabase();
        var clock = provider.GetRequiredService<TimeProvider>();
        var raw = provider.GetRequiredService<IDistributedCache>();
        var holds = provider.GetRequiredService<IHoldCache>();
        var seats = provider.GetRequiredService<ISeatAvailabilityCache>();
        var hold = new HoldResponse(Guid.NewGuid(), Guid.NewGuid(), clock.GetUtcNow().AddMinutes(-1), 0, 100m, "UAH", []);
        var map = new SessionSeatMapResponse(Guid.NewGuid(), Guid.NewGuid(),
            [new(Guid.NewGuid(), Guid.NewGuid(), "A", 1, 0, 100m, "UAH", 0, 0)]);
        var key = new SeatAvailabilityCacheKey(map.SessionId, map.AuditoriumId,
            SeatAvailabilityRevision.Create(map.Seats.Select(seat => new SeatAvailabilityVersion(seat.Id, seat.Version))));
        var holdKey = $"hold:{hold.Id:D}";
        var mapKey = $"seat-availability:v1:{key.SessionId:D}:{key.AuditoriumId:D}:{key.Revision}";
        var live = new DistributedCacheEntryOptions { AbsoluteExpiration = clock.GetUtcNow().AddMinutes(1) };
        try
        {
            // Deliberately disagree with the payload's deadline; Redis considers these keys live.
            await raw.SetAsync(holdKey, JsonSerializer.SerializeToUtf8Bytes(hold), live).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(await holds.GetAsync(hold.Id));
            Assert.True(await redis.KeyExistsAsync(holdKey));
            await raw.SetAsync(mapKey, JsonSerializer.SerializeToUtf8Bytes(new
            {
                ExpiresAt = clock.GetUtcNow().AddMinutes(-1),
                Map = map
            }), live);
            Assert.Null(await seats.GetAsync(key));
            Assert.True(await redis.KeyExistsAsync(mapKey));

            var staleMap = map with { Seats = map.Seats.Select(seat => seat with { Status = 2, Version = 1 }).ToArray() };
            await raw.SetAsync(mapKey, JsonSerializer.SerializeToUtf8Bytes(new
            {
                ExpiresAt = clock.GetUtcNow().AddMinutes(1),
                Map = staleMap
            }), live);
            Assert.Null(await seats.GetAsync(key));
            await seats.SetAsync(key, map);
            Assert.Equal(map.Seats.ToArray(), (await seats.GetAsync(key))!.Seats.ToArray());
        }
        finally
        {
            await redis.KeyDeleteAsync(holdKey);
            await redis.KeyDeleteAsync(mapKey);
        }
    }
}
