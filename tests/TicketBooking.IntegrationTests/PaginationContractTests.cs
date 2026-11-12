using System.Text.Json;
using TicketBooking.Contracts;
using TicketBooking.Contracts.Administration;
using TicketBooking.Contracts.Booking;
using TicketBooking.Contracts.Catalogue;

namespace TicketBooking.IntegrationTests;

public class PaginationContractTests
{
    [Theory]
    [InlineData(1, 20, 0, 0, false, false)]
    [InlineData(2, 20, 0, 0, true, false)]
    [InlineData(1, 20, 1, 1, false, false)]
    [InlineData(1, 20, 20, 1, false, false)]
    [InlineData(1, 20, 21, 2, false, true)]
    [InlineData(2, 20, 21, 2, true, false)]
    [InlineData(2, 20, 60, 3, true, true)]
    [InlineData(4, 2, 5, 3, true, false)]
    [InlineData(1, 100, int.MaxValue, 21474837, false, true)]
    [InlineData(int.MaxValue, 1, int.MaxValue, int.MaxValue, true, false)]
    public void Pagination_UsesFilteredCountAndHandlesEmptyLastBeyondEndAndLargePages(
        int page, int pageSize, int totalCount, int totalPages, bool previous, bool next)
    {
        var events = new EventListResponse([], page, pageSize, totalCount);
        var sessions = new EventSessionListResponse([], page, pageSize, totalCount);
        var bookings = new BookingListResponse([], page, pageSize, totalCount);
        var adminBookings = new AdminBookingListResponse([], page, pageSize, totalCount);
        AssertNavigation(events, totalPages, previous, next);
        AssertNavigation(sessions, totalPages, previous, next);
        AssertNavigation(bookings, totalPages, previous, next);
        AssertNavigation(adminBookings, totalPages, previous, next);
    }

    [Fact]
    public void Pagination_EventAndSessionEnvelopesShareStableJsonFieldsAndRoundTrip()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var events = new EventListResponse([new EventSummaryResponse(Guid.NewGuid(), "Film", "Description", 90, 12)], 2, 1, 3);
        var sessions = new EventSessionListResponse([], 2, 1, 3);
        var eventJson = JsonSerializer.Serialize(events, options);
        var sessionJson = JsonSerializer.Serialize(sessions, options);
        foreach (var json in new[] { eventJson, sessionJson })
        {
            using var document = JsonDocument.Parse(json);
            Assert.Equal(new[] { "hasNextPage", "hasPreviousPage", "items", "page", "pageSize", "totalCount", "totalPages" },
                document.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
            Assert.Equal(3, document.RootElement.GetProperty("totalPages").GetInt32());
            Assert.True(document.RootElement.GetProperty("hasPreviousPage").GetBoolean());
            Assert.True(document.RootElement.GetProperty("hasNextPage").GetBoolean());
        }
        var eventCopy = JsonSerializer.Deserialize<EventListResponse>(eventJson, options)!;
        var sessionCopy = JsonSerializer.Deserialize<EventSessionListResponse>(sessionJson, options)!;
        Assert.Equal(events.Items, eventCopy.Items);
        Assert.Equal(events.TotalCount, eventCopy.TotalCount);
        AssertNavigation(eventCopy, 3, true, true);
        AssertNavigation(sessionCopy, 3, true, true);
    }

    private static void AssertNavigation<T>(PagedResponse<T> response, int totalPages, bool previous, bool next)
    {
        Assert.Equal(totalPages, response.TotalPages);
        Assert.Equal(previous, response.HasPreviousPage);
        Assert.Equal(next, response.HasNextPage);
    }
}
