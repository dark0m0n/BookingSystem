using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using TicketBooking.Application.Reservation;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Domain.Reservation;

namespace TicketBooking.Infrastructure.Caching;

public sealed class RedisHoldCache(IDistributedCache cache, TimeProvider clock, ILogger<RedisHoldCache> logger,
    RedisCacheTimeoutPolicy timeout) : IHoldCache
{
    public async Task<HoldResponse?> GetAsync(Guid holdId, CancellationToken cancellationToken = default)
    {
        try
        {
            var bytes = await timeout.ExecuteAsync(token => cache.GetAsync(Key(holdId), token), cancellationToken);
            if (bytes is null) return null;
            var hold = JsonSerializer.Deserialize<HoldResponse>(bytes);
            if (hold is not null && hold.Id == holdId && hold.Status == (int)SeatHoldStatus.Active &&
                hold.ExpiresAt > clock.GetUtcNow() && hold.Items is not null)
                return hold;
            // Lookup is read-only. The caller may replace/remove this payload only after its DB commit.
        }
        catch (Exception exception) when (RedisCacheTimeoutPolicy.IsUnavailable(exception) || exception is JsonException)
        {
            logger.LogWarning(exception, "Hold {HoldId} cache read failed; using PostgreSQL.", holdId);
        }
        return null;
    }

    public async Task SetAsync(HoldResponse hold, CancellationToken cancellationToken = default)
    {
        var remaining = hold.ExpiresAt - clock.GetUtcNow();
        if (hold.Status != (int)SeatHoldStatus.Active || remaining <= TimeSpan.Zero)
        {
            await RemoveAsync(hold.Id, cancellationToken);
            return;
        }

        try
        {
            await timeout.ExecuteAsync(token => cache.SetAsync(Key(hold.Id), JsonSerializer.SerializeToUtf8Bytes(hold), new DistributedCacheEntryOptions
            {
                // Fixed deadline: provider connection delays and subsequent fills must not restart the lifetime.
                AbsoluteExpiration = hold.ExpiresAt
            }, token), cancellationToken);
        }
        catch (Exception exception) when (RedisCacheTimeoutPolicy.IsUnavailable(exception))
        {
            logger.LogWarning(exception, "Hold {HoldId} cache write failed; PostgreSQL remains authoritative.", hold.Id);
        }
        catch (ArgumentOutOfRangeException exception) when (exception.ParamName == nameof(DistributedCacheEntryOptions.AbsoluteExpiration))
        {
            // Redis may connect only after the captured deadline has elapsed.
            await RemoveAsync(hold.Id, cancellationToken);
        }
    }

    public async Task RemoveAsync(Guid holdId, CancellationToken cancellationToken = default)
    {
        try
        {
            await timeout.ExecuteAsync(token => cache.RemoveAsync(Key(holdId), token), cancellationToken);
        }
        catch (Exception exception) when (RedisCacheTimeoutPolicy.IsUnavailable(exception))
        {
            logger.LogWarning(exception, "Hold {HoldId} cache removal failed; reads recheck PostgreSQL state.", holdId);
        }
    }

    private static string Key(Guid holdId) => $"hold:{holdId:D}";
}
