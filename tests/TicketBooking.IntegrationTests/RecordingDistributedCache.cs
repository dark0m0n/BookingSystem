using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace TicketBooking.IntegrationTests;

// Deliberately retains expired data so tests prove the application rejects it independently of Redis TTL.
internal sealed class RecordingDistributedCache : IDistributedCache
{
    public sealed record Entry(byte[] Bytes, DistributedCacheEntryOptions Options);
    public ConcurrentDictionary<string, Entry> Entries { get; } = new();
    public bool FailReads { get; set; }
    public bool FailWrites { get; set; }
    public bool FailRemovals { get; set; }
    public bool ExpireDuringWrite { get; set; }
    public Func<string, Task>? BeforeSetAsync { get; set; }
    public Func<string, Task>? BeforeGetAsync { get; set; }
    public Func<string, Task>? BeforeRemoveAsync { get; set; }
    public Action<string>? AfterSet { get; set; }
    public int Reads { get; private set; }
    public int Writes { get; private set; }
    public int Removals { get; private set; }

    public byte[]? Get(string key)
    {
        Reads++;
        if (FailReads) throw Unavailable();
        return Entries.TryGetValue(key, out var entry) ? entry.Bytes : null;
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (BeforeGetAsync is not null) await BeforeGetAsync(key);
        return Get(key);
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        Writes++;
        if (FailWrites) throw Unavailable();
        if (ExpireDuringWrite) throw new ArgumentOutOfRangeException(nameof(DistributedCacheEntryOptions.AbsoluteExpiration));
        Entries[key] = new Entry(value, options);
    }

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (BeforeSetAsync is not null) await BeforeSetAsync(key);
        Set(key, value, options);
        AfterSet?.Invoke(key);
    }

    public void Remove(string key)
    {
        Removals++;
        if (FailRemovals) throw Unavailable();
        Entries.TryRemove(key, out _);
    }

    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (BeforeRemoveAsync is not null) await BeforeRemoveAsync(key);
        Remove(key);
    }

    public void Refresh(string key) => throw new NotSupportedException("Hold expiry must never slide.");
    public Task RefreshAsync(string key, CancellationToken token = default) => throw new NotSupportedException("Hold expiry must never slide.");

    private static RedisConnectionException Unavailable() => new(ConnectionFailureType.UnableToConnect, "Cache unavailable for test.");
}
