using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using TicketBooking.Application.Reservation;

namespace TicketBooking.Infrastructure.Caching;

public static class HoldCacheRegistration
{
    public static IServiceCollection AddHoldCaching(this IServiceCollection services, IConfiguration configuration)
    {
        var policy = new RedisCachePolicyOptions();
        configuration.GetSection(RedisCachePolicyOptions.SectionName).Bind(policy);
        services.AddRedisCachePolicy(policy);
        var connectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        var connection = ConfigurationOptions.Parse(connectionString);
        connection.AbortOnConnectFail = false;
        connection.ConnectRetry = 1;
        connection.ConnectTimeout = (int)policy.ConnectionTimeout.TotalMilliseconds;
        connection.SyncTimeout = (int)policy.CommandTimeout.TotalMilliseconds;
        connection.AsyncTimeout = (int)policy.CommandTimeout.TotalMilliseconds;
        connection.BacklogPolicy = BacklogPolicy.FailFast;
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = connectionString;
            options.ConfigurationOptions = connection;
        });
        services.AddSingleton<IHoldCache, RedisHoldCache>();
        return services;
    }
}
