namespace TicketBooking.Contracts.Reservation;

public sealed record CreateHoldRequest(IReadOnlyList<Guid>? SessionSeatIds);
