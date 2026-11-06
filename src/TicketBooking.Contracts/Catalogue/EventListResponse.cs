namespace TicketBooking.Contracts.Catalogue;

public sealed record EventListResponse : PagedResponse<EventSummaryResponse>
{
    public EventListResponse(IReadOnlyList<EventSummaryResponse> items, int page, int pageSize, int totalCount)
        : base(items, page, pageSize, totalCount)
    {
    }
}
