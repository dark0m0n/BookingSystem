namespace TicketBooking.Contracts.Administration;

public sealed record SessionSeatResponse(Guid Id, Guid SeatId, string Row, int Number, int Type,
    decimal Price, string Currency, int Status, long Version);
