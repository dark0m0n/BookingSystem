using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TicketBooking.Infrastructure.Caching;

public sealed class RedisCachePolicyOptions
{
    public const string SectionName = "RedisCache";
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

    public void Validate()
    {
        if (OperationTimeout < TimeSpan.FromMilliseconds(10) || OperationTimeout > TimeSpan.FromSeconds(2))
            throw new InvalidOperationException("RedisCache:OperationTimeout must be between 10 milliseconds and two seconds.");
        if (ConnectionTimeout < TimeSpan.FromMilliseconds(50) || ConnectionTimeout > TimeSpan.FromSeconds(5))
            throw new InvalidOperationException("RedisCache:ConnectionTimeout must be between 50 milliseconds and five seconds.");
        if (CommandTimeout < TimeSpan.FromMilliseconds(50) || CommandTimeout > TimeSpan.FromSeconds(5))
            throw new InvalidOperationException("RedisCache:CommandTimeout must be between 50 milliseconds and five seconds.");
    }
}

public static class RedisCachePolicyRegistration
{
    public static IServiceCollection AddRedisCachePolicy(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new RedisCachePolicyOptions();
        configuration.GetSection(RedisCachePolicyOptions.SectionName).Bind(options);
        return services.AddRedisCachePolicy(options);
    }

    internal static IServiceCollection AddRedisCachePolicy(this IServiceCollection services, RedisCachePolicyOptions options)
    {
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<RedisCacheTimeoutPolicy>();
        return services;
    }
}
