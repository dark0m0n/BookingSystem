namespace TicketBooking.Contracts.Catalogue;

public sealed record SessionSeatMapResponse(Guid SessionId, Guid AuditoriumId, IReadOnlyList<SeatMapSeatResponse> Seats);
