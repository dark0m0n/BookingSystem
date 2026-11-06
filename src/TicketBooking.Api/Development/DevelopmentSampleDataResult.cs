namespace TicketBooking.Api.Development;

public sealed record DevelopmentSampleDataResult(Guid VenueId, Guid AuditoriumId, Guid EventId, Guid SessionId,
    int SeatCount, DateTimeOffset StartsAt, bool CreatedSession);
