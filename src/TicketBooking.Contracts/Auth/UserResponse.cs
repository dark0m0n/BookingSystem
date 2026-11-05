namespace TicketBooking.Contracts.Auth;

public sealed record UserResponse(Guid Id, string Email, string DisplayName);
