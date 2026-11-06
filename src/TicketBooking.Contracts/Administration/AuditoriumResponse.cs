namespace TicketBooking.Contracts.Administration;

public sealed record AuditoriumResponse(Guid Id, Guid VenueId, string Name);
