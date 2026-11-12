namespace TicketBooking.Contracts.Booking;

public sealed record BookingResponse(Guid Id, string Number, Guid SessionId, int Status,
    DateTimeOffset CreatedAt, DateTimeOffset SessionStartsAt, DateTimeOffset PaymentDeadline,
    DateTimeOffset CancellationDeadline, decimal TotalAmount, string Currency,
    IReadOnlyList<BookingItemResponse> Items);

public sealed record BookingItemResponse(Guid SessionSeatId, decimal Price, string Currency);
