namespace TicketBooking.Contracts.Administration;

public sealed record CreateAuditoriumRequest(Guid VenueId, string? Name);
