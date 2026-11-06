namespace TicketBooking.Contracts.Catalogue;

public sealed record SeatMapSeatResponse(Guid Id, Guid SeatId, string Row, int Number, int Type,
    decimal Price, string Currency, int Status, long Version);
