namespace TicketBooking.Contracts.Auth;

public sealed record LoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt, UserResponse User,
    string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);
