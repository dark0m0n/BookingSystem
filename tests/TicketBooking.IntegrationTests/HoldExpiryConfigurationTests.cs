using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TicketBooking.Api.Reservation;
using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.IntegrationTests;

public class HoldExpiryConfigurationTests
{
    [Theory]
    [InlineData("BatchSize", "0")]
    [InlineData("BatchSize", "1001")]
    [InlineData("PollInterval", "00:00:00")]
    [InlineData("PollInterval", "00:00:00.9999999")]
    [InlineData("PollInterval", "01:00:00.0000001")]
    public void Registration_RejectsUnboundedBatchOrPollingSettings(string key, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"HoldExpiry:{key}"] = value
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddHoldExpiryWorker(configuration));
    }

    [Fact]
    public void Registration_BindsStartupSnapshotAndUsesScopedDatabaseWork()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HoldExpiry:BatchSize"] = "2",
            ["HoldExpiry:PollInterval"] = "00:00:03"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddDbContext<TicketBookingDbContext>(options => options.UseNpgsql("Host=localhost;Database=metadata_only;Username=test"));
        services.AddScoped<SessionSeatAllocationWriter>();
        services.AddHoldExpiryWorker(configuration);
        configuration["HoldExpiry:BatchSize"] = "3";
        configuration.Reload();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        Assert.Single(provider.GetServices<IHostedService>().OfType<HoldExpiryWorker>());
        Assert.Equal(2, provider.GetRequiredService<HoldExpiryOptions>().BatchSize);
        Assert.Equal(TimeSpan.FromSeconds(3), provider.GetRequiredService<HoldExpiryOptions>().PollInterval);
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<HoldExpiryReleaser>());
    }

    [Fact]
    public void Registration_DefaultsBoundWorkAndPolling()
    {
        var services = new ServiceCollection().AddHoldExpiryProcessing(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        Assert.Equal(100, provider.GetRequiredService<HoldExpiryOptions>().BatchSize);
        Assert.Equal(TimeSpan.FromSeconds(5), provider.GetRequiredService<HoldExpiryOptions>().PollInterval);
    }
}
