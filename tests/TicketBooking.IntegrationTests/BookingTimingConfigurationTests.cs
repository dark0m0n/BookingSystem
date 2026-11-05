using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Api.Configuration;
using TicketBooking.Domain;

namespace TicketBooking.IntegrationTests;

public class BookingTimingConfigurationTests
{
    [Fact]
    public void Registration_BindsOverridesAndKeepsAValidatedStartupSnapshot()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["BookingTiming:HoldDuration"] = "00:03:00",
            ["BookingTiming:PaymentCompletionWindow"] = "00:07:00",
            ["BookingTiming:CancellationLeadTime"] = "00:30:00"
        });
        var services = new ServiceCollection().AddBookingTiming(configuration);
        configuration["BookingTiming:HoldDuration"] = "01:00:00";
        configuration.Reload();
        using var provider = services.BuildServiceProvider();

        var policy = provider.GetRequiredService<BookingTimingPolicy>();

        Assert.Equal(TimeSpan.FromMinutes(3), policy.HoldDuration);
        Assert.Equal(TimeSpan.FromMinutes(7), policy.PaymentCompletionWindow);
        Assert.Equal(TimeSpan.FromMinutes(30), policy.CancellationLeadTime);
        Assert.Same(policy, provider.GetRequiredService<BookingTimingPolicy>());
    }

    [Fact]
    public void Registration_WhenSectionIsAbsent_UsesDocumentedDefaults()
    {
        using var provider = new ServiceCollection().AddBookingTiming(Configuration(new Dictionary<string, string?>())).BuildServiceProvider();

        var policy = provider.GetRequiredService<BookingTimingPolicy>();

        Assert.Equal(TimeSpan.FromMinutes(10), policy.HoldDuration);
        Assert.Equal(TimeSpan.FromMinutes(15), policy.PaymentCompletionWindow);
        Assert.Equal(TimeSpan.FromHours(1), policy.CancellationLeadTime);
    }

    [Theory]
    [InlineData("HoldDuration", "00:00:00")]
    [InlineData("HoldDuration", "-00:01:00")]
    [InlineData("PaymentCompletionWindow", "00:00:00")]
    [InlineData("PaymentCompletionWindow", "-00:01:00")]
    [InlineData("CancellationLeadTime", "-00:01:00")]
    public void Registration_RejectsInvalidDurationsBeforeTheApiStarts(string setting, string value)
    {
        var configuration = Configuration(new Dictionary<string, string?> { [$"BookingTiming:{setting}"] = value });

        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection().AddBookingTiming(configuration));
    }

    [Fact]
    public void Registration_RejectsMalformedDurationText()
    {
        var configuration = Configuration(new Dictionary<string, string?> { ["BookingTiming:HoldDuration"] = "ten minutes" });

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddBookingTiming(configuration));
    }

    private static IConfigurationRoot Configuration(IDictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
