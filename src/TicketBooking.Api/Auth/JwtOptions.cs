using System.Text;

namespace TicketBooking.Api.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string SigningKey { get; init; } = string.Empty;
    public int AccessTokenMinutes { get; init; } = 15;
    public int RefreshTokenDays { get; init; } = 7;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience) ||
            string.IsNullOrWhiteSpace(SigningKey) || Encoding.UTF8.GetByteCount(SigningKey) < 32 ||
            AccessTokenMinutes is < 1 or > 1440 || RefreshTokenDays is < 1 or > 90)
        {
            throw new InvalidOperationException(
                "Jwt requires Issuer, Audience, a SigningKey of at least 32 UTF-8 bytes, AccessTokenMinutes between 1 and 1440, and RefreshTokenDays between 1 and 90.");
        }
    }
}
