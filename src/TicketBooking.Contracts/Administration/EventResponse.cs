namespace TicketBooking.Contracts.Administration;

public sealed record EventResponse(Guid Id, string Title, string Description, int DurationMinutes, int AgeRating, int Status);
