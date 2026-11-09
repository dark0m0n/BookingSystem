using System.Net.Sockets;
using StackExchange.Redis;

namespace TicketBooking.Infrastructure.Caching;

/// <summary>Bounds cache I/O waits independently of provider cancellation/heartbeat behavior.</summary>
public sealed class RedisCacheTimeoutPolicy(RedisCachePolicyOptions options, TimeProvider clock)
{
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<T>? pending = null;
        try
        {
            pending = operation(cancellationToken);
            return await pending.WaitAsync(options.OperationTimeout, clock, cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The Redis provider cancelled its operation.", exception);
        }
        finally
        {
            if (pending is not null)
            {
                // A provider may ignore cancellation. Observe late failures without delaying database fallback.
                if (pending.IsCompleted) _ = pending.Exception;
                else
                    _ = pending.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }

    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        ExecuteAsync(async token => { await operation(token); return true; }, cancellationToken);

    internal static bool IsUnavailable(Exception exception) =>
        exception is RedisException or TimeoutException or IOException or SocketException;
}
