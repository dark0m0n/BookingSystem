namespace TicketBooking.Contracts.Administration;

public sealed record SeatResponse(Guid Id, Guid AuditoriumId, string Row, int Number, int Type);
