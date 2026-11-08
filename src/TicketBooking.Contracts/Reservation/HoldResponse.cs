namespace TicketBooking.Contracts.Reservation;

public sealed record HoldResponse(Guid Id, Guid SessionId, DateTimeOffset ExpiresAt, int Status,
    decimal TotalAmount, string Currency, IReadOnlyList<HoldItemResponse> Items);

public sealed record HoldItemResponse(Guid SessionSeatId, decimal Price, string Currency);
