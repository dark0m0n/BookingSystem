namespace TicketBooking.Contracts.Administration;

public sealed record SeatRequest(string? Row, int Number, int Type = 0);
