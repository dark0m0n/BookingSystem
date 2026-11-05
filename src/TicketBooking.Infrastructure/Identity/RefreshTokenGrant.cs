namespace TicketBooking.Infrastructure.Identity;

public sealed record RefreshTokenGrant(string Token, DateTimeOffset ExpiresAt, ApplicationUser User, IReadOnlyList<string> Roles);
