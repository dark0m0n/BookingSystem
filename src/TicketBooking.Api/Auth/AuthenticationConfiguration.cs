using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using TicketBooking.Application.Identity;
using TicketBooking.Domain.Identity;
using TicketBooking.Infrastructure.Identity;

namespace TicketBooking.Api.Auth;

public static class AuthenticationConfiguration
{
    public static IServiceCollection AddTicketBookingAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        settings.Validate();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<ResourceOwnershipService>();
        services.AddSingleton(settings);
        services.AddScoped<JwtTokenIssuer>();
        services.AddSingleton(new RefreshTokenPolicy(TimeSpan.FromDays(settings.RefreshTokenDays)));
        services.AddScoped<RefreshTokenService>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<TimeProvider>((options, clock) =>
            {
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    RequireAudience = true,
                    IgnoreTrailingSlashWhenValidatingAudience = false,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = "role",
                    LifetimeValidator = (notBefore, expires, _, _) =>
                    {
                        var now = clock.GetUtcNow().UtcDateTime;
                        return expires.HasValue && now < expires.Value && (!notBefore.HasValue || now >= notBefore.Value);
                    }
                };
            });
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthorizationPolicies.Customer, policy =>
                policy.RequireAuthenticatedUser().RequireRole(RoleNames.Customer, RoleNames.Admin));
            options.AddPolicy(AuthorizationPolicies.Admin, policy =>
                policy.RequireAuthenticatedUser().RequireRole(RoleNames.Admin));
        });
        return services;
    }
}
