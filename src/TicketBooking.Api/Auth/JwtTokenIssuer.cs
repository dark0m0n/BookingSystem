using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TicketBooking.Infrastructure.Identity;

namespace TicketBooking.Api.Auth;

public sealed class JwtTokenIssuer(JwtOptions options, TimeProvider clock)
{
    public IssuedAccessToken Issue(ApplicationUser user, IEnumerable<string> roles)
    {
        // JWT numeric dates have whole-second precision; expose the same expiry in the response.
        var now = DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds());
        var expiresAt = now.AddMinutes(options.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)
        };
        claims.AddRange(roles.Select(role => new Claim("role", role)));
        var token = new JwtSecurityToken(options.Issuer, options.Audience, claims, now.UtcDateTime, expiresAt.UtcDateTime,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)), SecurityAlgorithms.HmacSha256));
        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
