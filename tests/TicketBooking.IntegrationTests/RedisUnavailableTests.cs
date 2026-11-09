using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Application.Catalogue;
using TicketBooking.Application.Reservation;
using TicketBooking.Contracts.Catalogue;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Infrastructure.Caching;

namespace TicketBooking.IntegrationTests;

public class RedisUnavailableTests
{
    [Fact]
    public async Task RealDisconnectedClient_ReturnsMissesAndSkipsMutationsWithinTheBound()
    {
        // Reserve a local port without listening: no Redis service can occupy this endpoint during the test.
        using var unavailable = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        unavailable.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)unavailable.LocalEndPoint!).Port;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = $"127.0.0.1:{port}",
            ["RedisCache:OperationTimeout"] = "00:00:00.100",
            ["RedisCache:ConnectionTimeout"] = "00:00:00.100",
            ["RedisCache:CommandTimeout"] = "00:00:00.100",
            ["SeatAvailabilityCache:MinimumSeatCount"] = "1"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddHoldCaching(configuration);
        services.AddSeatAvailabilityCaching(configuration);
        using var provider = services.BuildServiceProvider();
        var clock = provider.GetRequiredService<TimeProvider>();
        var holds = provider.GetRequiredService<IHoldCache>();
        var seats = provider.GetRequiredService<ISeatAvailabilityCache>();
        var hold = new HoldResponse(Guid.NewGuid(), Guid.NewGuid(), clock.GetUtcNow().AddMinutes(10), 0, 100m, "UAH", []);
        var map = new SessionSeatMapResponse(Guid.NewGuid(), Guid.NewGuid(),
            [new(Guid.NewGuid(), Guid.NewGuid(), "A", 1, 0, 100m, "UAH", 0, 0)]);
        var key = new SeatAvailabilityCacheKey(map.SessionId, map.AuditoriumId,
            SeatAvailabilityRevision.Create(map.Seats.Select(seat => new SeatAvailabilityVersion(seat.Id, seat.Version))));
        // A generous watchdog detects an unbounded wait; exact timeout edges have deterministic timer tests.
        var watchdog = TimeSpan.FromSeconds(3);
        Assert.Null(await holds.GetAsync(hold.Id).WaitAsync(watchdog));
        await holds.SetAsync(hold).WaitAsync(watchdog);
        await holds.RemoveAsync(hold.Id).WaitAsync(watchdog);
        Assert.Null(await seats.GetAsync(key).WaitAsync(watchdog));
        await seats.SetAsync(key, map).WaitAsync(watchdog);
    }
}
