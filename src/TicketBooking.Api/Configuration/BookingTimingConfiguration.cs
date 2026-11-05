using TicketBooking.Domain;

namespace TicketBooking.Api.Configuration;

public static class BookingTimingConfiguration
{
    public static IServiceCollection AddBookingTiming(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new BookingTimingOptions();
        configuration.GetSection(BookingTimingOptions.SectionName).Bind(options);

        // Bind and validate once at startup; reloads must not silently change active policies.
        var policy = new BookingTimingPolicy(options.HoldDuration, options.PaymentCompletionWindow, options.CancellationLeadTime);
        return services.AddSingleton(policy);
    }
}
