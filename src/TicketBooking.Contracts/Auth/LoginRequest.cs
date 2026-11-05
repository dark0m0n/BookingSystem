namespace TicketBooking.Contracts.Auth;

public sealed record LoginRequest(string? Email, string? Password);
