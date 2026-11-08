using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.Api.Reservation;

public sealed class HoldExpiryWorker(HoldExpiryBatchProcessor processor, HoldExpiryOptions options,
    TimeProvider clock, ILogger<HoldExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        HoldExpiryCursor? cursor = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await processor.ProcessBatchAsync(cursor, stoppingToken);
                cursor = result.NextCursor;
                logger.LogDebug(new EventId(1, "HoldExpiryBatchCompleted"),
                    "Hold expiry batch examined {Examined} holds and expired {Expired}.", result.Examined, result.Expired);
                await Task.Delay(options.PollInterval, clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Hold expiry scan failed; retrying after the polling interval.");
                try { await Task.Delay(options.PollInterval, clock, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            }
        }
    }
}

public static class HoldExpiryRegistration
{
    public static IServiceCollection AddHoldExpiryProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new HoldExpiryOptions();
        configuration.GetSection(HoldExpiryOptions.SectionName).Bind(options);
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<HoldExpiryBatchProcessor>();
        services.AddScoped<HoldExpiryReleaser>();
        return services;
    }

    public static IServiceCollection AddHoldExpiryWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHoldExpiryProcessing(configuration);
        return services.AddHostedService<HoldExpiryWorker>();
    }
}
