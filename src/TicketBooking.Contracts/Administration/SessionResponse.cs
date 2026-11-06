namespace TicketBooking.Contracts.Administration;

public sealed record SessionResponse(Guid Id, Guid EventId, Guid AuditoriumId, DateTimeOffset StartsAt,
    DateTimeOffset EndsAt, int Status, IReadOnlyList<SessionSeatResponse> Seats);
