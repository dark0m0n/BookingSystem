namespace TicketBooking.Contracts.Administration;

public sealed record UpdateSessionRequest(Guid? EventId = null, Guid? AuditoriumId = null, DateTimeOffset? StartsAt = null);
