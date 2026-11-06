namespace TicketBooking.Contracts.Administration;

public sealed record UpdateEventRequest(string? Title = null, string? Description = null, int? DurationMinutes = null,
    int? AgeRating = null, int? Status = null);
