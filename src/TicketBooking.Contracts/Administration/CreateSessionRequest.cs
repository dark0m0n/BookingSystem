namespace TicketBooking.Contracts.Administration;

public sealed record CreateSessionRequest(Guid EventId, Guid AuditoriumId, DateTimeOffset? StartsAt,
    decimal? Price, string? Currency);
