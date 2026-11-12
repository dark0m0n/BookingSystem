namespace TicketBooking.Contracts.Booking;

public sealed record BookingListResponse : PagedResponse<BookingSummaryResponse>
{
    public BookingListResponse(IReadOnlyList<BookingSummaryResponse> items, int page, int pageSize, int totalCount)
        : base(items, page, pageSize, totalCount)
    {
    }
}

public sealed record BookingSummaryResponse(Guid Id, string Number, Guid SessionId, int Status,
    DateTimeOffset CreatedAt, DateTimeOffset SessionStartsAt, DateTimeOffset PaymentDeadline,
    DateTimeOffset CancellationDeadline, decimal TotalAmount, string Currency);
