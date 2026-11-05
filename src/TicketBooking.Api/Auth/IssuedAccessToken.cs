namespace TicketBooking.Api.Auth;

public sealed record IssuedAccessToken(string AccessToken, DateTimeOffset ExpiresAt);
