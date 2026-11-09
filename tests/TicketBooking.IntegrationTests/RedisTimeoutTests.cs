using System.Net.Sockets;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TicketBooking.Application.Catalogue;
using TicketBooking.Contracts.Catalogue;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Infrastructure.Caching;

namespace TicketBooking.IntegrationTests;

public class RedisTimeoutTests
{
    private readonly TimerClock clock = new();
    private readonly RecordingDistributedCache storage = new();
    private RedisCacheTimeoutPolicy Policy => new(new RedisCachePolicyOptions(), clock);
    private RedisHoldCache Holds => new(storage, clock, NullLogger<RedisHoldCache>.Instance, Policy);
    private RedisSeatAvailabilityCache Seats => new(storage, clock, new SeatAvailabilityCacheOptions { MinimumSeatCount = 1 },
        NullLogger<RedisSeatAvailabilityCache>.Instance, Policy);
    private HoldResponse Hold => new(Guid.NewGuid(), Guid.NewGuid(), clock.GetUtcNow().AddMinutes(10), 0, 100m, "UAH", []);
    private static SessionSeatMapResponse Map => new(Guid.NewGuid(), Guid.NewGuid(),
        [new(Guid.NewGuid(), Guid.NewGuid(), "A", 1, 0, 100m, "UAH", 0, 0)]);
    private static SeatAvailabilityCacheKey Key(SessionSeatMapResponse map) => new(map.SessionId, map.AuditoriumId,
        SeatAvailabilityRevision.Create(map.Seats.Select(seat => new SeatAvailabilityVersion(seat.Id, seat.Version))));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonCooperativeRead_TimesOutAtItsBudgetAndCanRecover(bool seats)
    {
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.BeforeGetAsync = _ => blocked.Task; // Deliberately ignores the cancellation token.
        var hold = Hold;
        var map = Map;
        async Task<bool> Read() => seats ? await Seats.GetAsync(Key(map)) is not null : await Holds.GetAsync(hold.Id) is not null;
        var pending = Read();
        Assert.True(clock.TimerCount > 0);
        clock.Advance(TimeSpan.FromMilliseconds(250).Subtract(TimeSpan.FromTicks(1)));
        Assert.False(pending.IsCompleted);
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(blocked.Task.IsCompleted);
        // A late provider fault is observed by the policy, while subsequent calls can succeed.
        blocked.SetException(new IOException("Late network failure."));
        storage.BeforeGetAsync = null;
        if (seats) await Seats.SetAsync(Key(map), map);
        else await Holds.SetAsync(hold);
        Assert.True(await Read());
    }

    [Theory]
    [InlineData("hold-write")]
    [InlineData("hold-remove")]
    [InlineData("seat-write")]
    public async Task NonCooperativeMutations_AreSkippedAfterTheBound(string operation)
    {
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hold = Hold;
        var map = Map;
        if (operation == "hold-remove") await Holds.SetAsync(hold);
        storage.BeforeSetAsync = storage.BeforeRemoveAsync = _ => blocked.Task;
        var pending = operation switch
        {
            "hold-write" => Holds.SetAsync(hold),
            "hold-remove" => Holds.RemoveAsync(hold.Id),
            _ => Seats.SetAsync(Key(map), map)
        };
        clock.Advance(TimeSpan.FromMilliseconds(250));
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(blocked.Task.IsCompleted);
        Assert.Equal(operation == "hold-remove" ? 1 : 0, storage.Entries.Count);
        blocked.SetException(new IOException("Late provider failure."));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellation_InterruptsANonCooperativeProviderWithoutFallback(bool seats)
    {
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.BeforeGetAsync = _ => blocked.Task;
        using var cancellation = new CancellationTokenSource();
        var map = Map;
        Task pending = seats ? Seats.GetAsync(Key(map), cancellation.Token) : Holds.GetAsync(Hold.Id, cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        blocked.SetException(new IOException("Late provider failure."));
    }

    [Theory]
    [InlineData("io", false)]
    [InlineData("io", true)]
    [InlineData("socket", false)]
    [InlineData("socket", true)]
    [InlineData("cancel", false)]
    [InlineData("cancel", true)]
    public async Task TransportFailuresAndProviderCancellation_AreCacheMisses(string failure, bool seats)
    {
        storage.BeforeGetAsync = _ => failure switch
        {
            "io" => Task.FromException(new IOException("Network failure.")),
            "socket" => Task.FromException(new SocketException((int)SocketError.ConnectionReset)),
            _ => Task.FromCanceled(new CancellationToken(true))
        };
        if (seats) Assert.Null(await Seats.GetAsync(Key(Map)));
        else Assert.Null(await Holds.GetAsync(Hold.Id));
    }

    [Fact]
    public async Task SynchronousProviderCancellation_IsNormalizedToATimeout()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => Policy.ExecuteAsync<byte[]?>(
            _ => throw new OperationCanceledException("Provider cancelled before returning a task."), CancellationToken.None));
    }

    [Fact]
    public async Task ProgrammingErrors_AreNotHiddenAsCacheMisses()
    {
        storage.BeforeGetAsync = _ => Task.FromException(new InvalidOperationException("Unexpected programming error."));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Holds.GetAsync(Hold.Id));
    }

    [Fact]
    public void Registration_EnforcesLimitsForCustomConnectionStringsWithoutConnecting()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = "localhost:16379,abortConnect=true,connectRetry=8,connectTimeout=60000,syncTimeout=60000,asyncTimeout=60000",
            ["RedisCache:OperationTimeout"] = "00:00:00.075",
            ["RedisCache:ConnectionTimeout"] = "00:00:00.100",
            ["RedisCache:CommandTimeout"] = "00:00:00.150"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddHoldCaching(configuration);
        using var provider = services.BuildServiceProvider();
        var connection = provider.GetRequiredService<IOptions<RedisCacheOptions>>().Value.ConfigurationOptions!;
        Assert.Equal(100, connection.ConnectTimeout);
        Assert.Equal(150, connection.AsyncTimeout);
        Assert.Equal(150, connection.SyncTimeout);
        Assert.Equal(1, connection.ConnectRetry);
        Assert.False(connection.AbortOnConnectFail);
        Assert.Same(BacklogPolicy.FailFast, connection.BacklogPolicy);
        Assert.Equal(TimeSpan.FromMilliseconds(75), provider.GetRequiredService<RedisCachePolicyOptions>().OperationTimeout);
    }

    [Theory]
    [InlineData("OperationTimeout", "00:00:00.009")]
    [InlineData("OperationTimeout", "00:00:02.0000001")]
    [InlineData("ConnectionTimeout", "00:00:00.049")]
    [InlineData("ConnectionTimeout", "00:00:05.0000001")]
    [InlineData("CommandTimeout", "00:00:00.049")]
    [InlineData("CommandTimeout", "00:00:05.0000001")]
    public void InvalidTimeouts_FailDuringRegistration(string setting, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"RedisCache:{setting}"] = value
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddHoldCaching(configuration));
    }

    // Only test time moves; deadline tests use no sleeps or real-time expiration timers.
    private sealed class TimerClock : TimeProvider
    {
        private readonly List<ControlledTimer> timers = [];
        private DateTimeOffset now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public int TimerCount => timers.Count;
        public override DateTimeOffset GetUtcNow() => now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ControlledTimer(this, callback, state);
            timer.Change(dueTime, period);
            timers.Add(timer);
            return timer;
        }
        public void Advance(TimeSpan elapsed)
        {
            now += elapsed;
            foreach (var timer in timers.ToArray()) timer.FireIfDue();
        }
        private sealed class ControlledTimer(TimerClock owner, TimerCallback callback, object? state) : ITimer
        {
            private DateTimeOffset due = DateTimeOffset.MaxValue;
            private TimeSpan period;
            private bool disposed;
            public bool Change(TimeSpan dueTime, TimeSpan newPeriod)
            {
                if (disposed) return false;
                due = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : owner.now + dueTime;
                period = newPeriod;
                return true;
            }
            public void FireIfDue()
            {
                if (disposed || due > owner.now) return;
                due = period == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : owner.now + period;
                callback(state);
            }
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
