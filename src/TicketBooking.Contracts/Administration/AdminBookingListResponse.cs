namespace TicketBooking.Contracts.Administration;

public sealed record AdminBookingListResponse : PagedResponse<AdminBookingSummaryResponse>
{
    public AdminBookingListResponse(IReadOnlyList<AdminBookingSummaryResponse> items, int page, int pageSize, int totalCount)
        : base(items, page, pageSize, totalCount)
    {
    }
}

public sealed record AdminBookingSummaryResponse(Guid Id, string Number, Guid UserId, Guid SessionId, int Status,
    DateTimeOffset CreatedAt, DateTimeOffset SessionStartsAt, DateTimeOffset PaymentDeadline,
    DateTimeOffset CancellationDeadline, decimal TotalAmount, string Currency);
