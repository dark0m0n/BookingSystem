using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TicketBooking.Api.Booking;
using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.IntegrationTests;

public class BookingExpiryConfigurationTests
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
            [$"BookingExpiry:{key}"] = value
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddBookingExpiryWorker(configuration));
    }

    [Fact]
    public void Registration_BindsStartupSnapshotAndUsesScopedDatabaseWork()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BookingExpiry:BatchSize"] = "2",
            ["BookingExpiry:PollInterval"] = "00:00:03"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddDbContext<TicketBookingDbContext>(options => options.UseNpgsql("Host=localhost;Database=metadata_only;Username=test"));
        services.AddScoped<SessionSeatAllocationWriter>();
        services.AddBookingExpiryWorker(configuration);
        configuration["BookingExpiry:BatchSize"] = "3";
        configuration.Reload();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        Assert.Single(provider.GetServices<IHostedService>().OfType<BookingExpiryWorker>());
        Assert.Equal(2, provider.GetRequiredService<BookingExpiryOptions>().BatchSize);
        Assert.Equal(TimeSpan.FromSeconds(3), provider.GetRequiredService<BookingExpiryOptions>().PollInterval);
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<BookingExpiryReleaser>());
    }

    [Fact]
    public void Registration_DefaultsBoundWorkAndPolling()
    {
        var services = new ServiceCollection().AddBookingExpiryProcessing(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        Assert.Equal(100, provider.GetRequiredService<BookingExpiryOptions>().BatchSize);
        Assert.Equal(TimeSpan.FromSeconds(5), provider.GetRequiredService<BookingExpiryOptions>().PollInterval);
    }
}
