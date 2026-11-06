namespace TicketBooking.Contracts.Administration;

public sealed record CreateEventRequest(string? Title, string? Description, int DurationMinutes, int AgeRating,
    int Status = 0);
