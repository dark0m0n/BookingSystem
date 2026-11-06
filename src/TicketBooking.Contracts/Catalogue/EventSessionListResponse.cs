namespace TicketBooking.Contracts.Catalogue;

public sealed record EventSessionListResponse : PagedResponse<SessionDetailsResponse>
{
    public EventSessionListResponse(IReadOnlyList<SessionDetailsResponse> items, int page, int pageSize, int totalCount)
        : base(items, page, pageSize, totalCount)
    {
    }
}
