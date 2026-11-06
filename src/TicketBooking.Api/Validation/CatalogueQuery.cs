using System.Globalization;

namespace TicketBooking.Api.Validation;

public sealed record CatalogueQuery(string? Query, string? Date, string? VenueId, string? Page, string? PageSize,
    IReadOnlyList<string> RepeatedParameters)
{
    public static CatalogueQuery Bind(IQueryCollection query, bool includeText)
    {
        var repeated = new List<string>();
        string? Read(string name)
        {
            if (!query.TryGetValue(name, out var values)) return null;
            if (values.Count == 1) return values[0];
            repeated.Add(name);
            return null;
        }
        return new CatalogueQuery(includeText ? Read("query") : null, Read("date"), Read("venueId"), Read("page"), Read("pageSize"), repeated);
    }

    public (int Page, int PageSize, long Offset, DateOnly? Date, Guid? VenueId) Parse()
    {
        var page = Page is null ? 1 : int.Parse(Page, CultureInfo.InvariantCulture);
        var pageSize = PageSize is null ? 20 : int.Parse(PageSize, CultureInfo.InvariantCulture);
        return (page, pageSize, ((long)page - 1) * pageSize,
            Date is null ? null : DateOnly.ParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            VenueId is null ? null : Guid.Parse(VenueId));
    }
}
