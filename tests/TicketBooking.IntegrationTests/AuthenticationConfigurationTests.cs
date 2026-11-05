using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Api.Auth;

namespace TicketBooking.IntegrationTests;

public class AuthenticationConfigurationTests
{
    [Theory]
    [InlineData("Jwt:SigningKey", null)]
    [InlineData("Jwt:SigningKey", "short")]
    [InlineData("Jwt:Issuer", " ")]
    [InlineData("Jwt:Audience", "")]
    [InlineData("Jwt:AccessTokenMinutes", "0")]
    [InlineData("Jwt:AccessTokenMinutes", "-1")]
    [InlineData("Jwt:AccessTokenMinutes", "1441")]
    [InlineData("Jwt:RefreshTokenDays", "0")]
    [InlineData("Jwt:RefreshTokenDays", "91")]
    public void Authentication_RejectsInvalidSettingsAtRegistration(string key, string? value)
    {
        var values = new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = "test-only-signing-key-with-at-least-32-bytes",
            ["Jwt:Issuer"] = "test-issuer",
            ["Jwt:Audience"] = "test-audience",
            ["Jwt:AccessTokenMinutes"] = "15",
            [key] = value
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddTicketBookingAuthentication(configuration));

        Assert.DoesNotContain("test-only-signing-key", exception.Message);
    }
}
