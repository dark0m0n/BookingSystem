using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Api.Auth;

namespace TicketBooking.IntegrationTests;

public class AuthorizationPolicyTests
{
    [Theory]
    [InlineData("Customer", true, "Customer", true)]
    [InlineData("Customer", true, "Admin", true)]
    [InlineData("Customer", true, null, false)]
    [InlineData("Customer", true, "Unrelated", false)]
    [InlineData("Customer", false, "Customer", false)]
    [InlineData("Admin", true, "Admin", true)]
    [InlineData("Admin", true, "Customer", false)]
    [InlineData("Admin", true, null, false)]
    [InlineData("Admin", true, "admin", false)]
    [InlineData("Admin", false, "Admin", false)]
    public async Task NamedPolicies_RequireAuthenticatedServerRoles(string policy, bool authenticated, string? role, bool allowed)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "policy-tests",
            ["Jwt:Audience"] = "policy-tests",
            ["Jwt:SigningKey"] = "test-only-policy-signing-key-with-at-least-32-bytes"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddTicketBookingAuthentication(configuration);
        await using var provider = services.BuildServiceProvider();
        var claims = role is null ? Array.Empty<Claim>() : [new Claim("role", role)];
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "test" : null, "sub", "role"));

        var result = await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy);

        Assert.Equal(allowed, result.Succeeded);
    }
}
