namespace TicketBooking.Contracts.Administration;

public sealed record AddSeatsRequest(IReadOnlyList<SeatRequest?>? Seats);
