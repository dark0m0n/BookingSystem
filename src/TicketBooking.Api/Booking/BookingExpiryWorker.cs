using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.Api.Booking;

public sealed class BookingExpiryWorker(BookingExpiryBatchProcessor processor, BookingExpiryOptions options,
    TimeProvider clock, ILogger<BookingExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        BookingExpiryCursor? cursor = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await processor.ProcessBatchAsync(cursor, stoppingToken);
                cursor = result.NextCursor;
                logger.LogDebug(new EventId(1, "BookingExpiryBatchCompleted"),
                    "Booking expiry batch examined {Examined} bookings and expired {Expired}.", result.Examined, result.Expired);
                await Task.Delay(options.PollInterval, clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Booking expiry scan failed; retrying after the polling interval.");
                try { await Task.Delay(options.PollInterval, clock, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            }
        }
    }
}

public static class BookingExpiryRegistration
{
    public static IServiceCollection AddBookingExpiryProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new BookingExpiryOptions();
        configuration.GetSection(BookingExpiryOptions.SectionName).Bind(options);
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<BookingExpiryBatchProcessor>();
        services.AddScoped<BookingExpiryReleaser>();
        return services;
    }

    public static IServiceCollection AddBookingExpiryWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddBookingExpiryProcessing(configuration);
        return services.AddHostedService<BookingExpiryWorker>();
    }
}
