using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TicketBooking.Application.Catalogue;
using TicketBooking.Contracts.Catalogue;

namespace TicketBooking.Infrastructure.Caching;

public sealed class RedisSeatAvailabilityCache(IDistributedCache cache, TimeProvider clock,
    SeatAvailabilityCacheOptions options, ILogger<RedisSeatAvailabilityCache> logger, RedisCacheTimeoutPolicy timeout) : ISeatAvailabilityCache
{
    private sealed record Entry(DateTimeOffset ExpiresAt, SessionSeatMapResponse Map);

    public bool IsUseful(int seatCount) => options.Enabled && seatCount >= options.MinimumSeatCount;

    public async Task<SessionSeatMapResponse?> GetAsync(SeatAvailabilityCacheKey key, CancellationToken cancellationToken = default)
    {
        try
        {
            var bytes = await timeout.ExecuteAsync(token => cache.GetAsync(Key(key), token), cancellationToken);
            if (bytes is null) return null;
            var entry = JsonSerializer.Deserialize<Entry>(bytes);
            if (entry is not null && entry.ExpiresAt > clock.GetUtcNow() && Matches(key, entry.Map))
                return entry.Map;
        }
        catch (Exception exception) when (RedisCacheTimeoutPolicy.IsUnavailable(exception) || exception is JsonException)
        {
            logger.LogWarning(exception, "Session {SessionId} seat cache read failed; using PostgreSQL.", key.SessionId);
        }
        return null;
    }

    public async Task SetAsync(SeatAvailabilityCacheKey key, SessionSeatMapResponse map, CancellationToken cancellationToken = default)
    {
        if (!IsUseful(map.Seats.Count) || !Matches(key, map)) return;
        var expiresAt = clock.GetUtcNow().Add(options.Lifetime);
        try
        {
            await timeout.ExecuteAsync(token => cache.SetAsync(Key(key), JsonSerializer.SerializeToUtf8Bytes(new Entry(expiresAt, map)),
                new DistributedCacheEntryOptions { AbsoluteExpiration = expiresAt }, token), cancellationToken);
        }
        catch (Exception exception) when (RedisCacheTimeoutPolicy.IsUnavailable(exception))
        {
            logger.LogWarning(exception, "Session {SessionId} seat cache write failed; PostgreSQL remains authoritative.", key.SessionId);
        }
        catch (ArgumentOutOfRangeException exception) when (exception.ParamName == nameof(DistributedCacheEntryOptions.AbsoluteExpiration))
        {
            // A slow connection may outlive this short cache lifetime. Skip the write.
        }
    }

    private static bool Matches(SeatAvailabilityCacheKey key, SessionSeatMapResponse? map) =>
        map is not null && map.SessionId == key.SessionId && map.AuditoriumId == key.AuditoriumId &&
        map.Seats is not null && map.Seats.All(seat => seat is not null) &&
        SeatAvailabilityRevision.Create(map.Seats.Select(seat => new SeatAvailabilityVersion(seat.Id, seat.Version))) == key.Revision;

    private static string Key(SeatAvailabilityCacheKey key) =>
        $"seat-availability:v1:{key.SessionId:D}:{key.AuditoriumId:D}:{key.Revision}";
}

public static class SeatAvailabilityCacheRegistration
{
    public static IServiceCollection AddSeatAvailabilityCaching(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new SeatAvailabilityCacheOptions();
        configuration.GetSection(SeatAvailabilityCacheOptions.SectionName).Bind(options);
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<ISeatAvailabilityCache, RedisSeatAvailabilityCache>();
        return services;
    }
}
