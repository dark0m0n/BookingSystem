namespace TicketBooking.Contracts.Catalogue;

public sealed record EventSummaryResponse(Guid Id, string Title, string Description, int DurationMinutes, int AgeRating);
