namespace TicketBooking.Contracts.Catalogue;

public sealed record SessionDetailsResponse(Guid Id, Guid EventId, string EventTitle, Guid AuditoriumId,
    string AuditoriumName, Guid VenueId, string VenueName, string VenueAddress, DateTimeOffset StartsAt,
    DateTimeOffset EndsAt, int Status);
