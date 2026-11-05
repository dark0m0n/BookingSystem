using TicketBooking.Application.Identity;

namespace TicketBooking.Api.Auth;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var identities = accessor.HttpContext?.User.Identities.Where(identity => identity.IsAuthenticated).ToArray();
            if (identities is not { Length: 1 })
            {
                return null;
            }
            // Inbound JWT claim mapping is disabled. Do not fall back to request IDs or other claims.
            var subjects = identities[0].FindAll("sub").ToArray();
            return subjects.Length == 1 && Guid.TryParseExact(subjects[0].Value, "D", out var id) && id != Guid.Empty
                ? id
                : null;
        }
    }
}
