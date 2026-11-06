using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Catalogue;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Venue;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;
using VenueEntity = TicketBooking.Domain.Venue.Venue;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresFact]
    public async Task PublicEvents_AnonymousRequestReturnsPublishedEventsWithDefaultPagination()
    {
        await SeedPublicEvents();
        using var response = await client.GetAsync("/api/events");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = (await response.Content.ReadFromJsonAsync<EventListResponse>())!;
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(4, page.TotalCount);
        Assert.Equal(new[] { "Alpha", "Beta", "Gamma", "Unscheduled" }, page.Items.Select(e => e.Title));
        Assert.All(page.Items, e =>
        {
            Assert.NotEqual(Guid.Empty, e.Id);
            Assert.Equal(90, e.DurationMinutes);
            Assert.Equal(12, e.AgeRating);
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Draft title", body);
        Assert.DoesNotContain("Archived title", body);
        Assert.DoesNotContain("userId", body);
        Assert.DoesNotContain("seats", body);
    }

    [PostgresTheory]
    [InlineData("query=%20aLpH%20", "Alpha")]
    [InlineData("query=SPACE", "Alpha,Gamma")]
    [InlineData("query=%20%20", "Alpha,Beta,Gamma,Unscheduled")]
    [InlineData("query=does-not-exist", "")]
    [InlineData("date=2030-01-02", "Alpha,Beta")]
    [InlineData("date=9999-12-31", "")]
    public async Task PublicEvents_FiltersByLiteralTextAndUtcSessionDate(string query, string expected)
    {
        await SeedPublicEvents();
        var page = await ReadPublicEvents(query);
        Assert.Equal(expected.Length == 0 ? [] : expected.Split(','), page.Items.Select(e => e.Title));
        Assert.Equal(page.Items.Count, page.TotalCount);
    }

    [PostgresFact]
    public async Task PublicEvents_DateAndVenueMustMatchTheSameScheduledSessionWithoutDuplicateEvents()
    {
        var venueId = await SeedPublicEvents();
        var venue = await ReadPublicEvents($"venueId={venueId}");
        Assert.Equal(new[] { "Alpha", "Beta" }, venue.Items.Select(e => e.Title));
        var combined = await ReadPublicEvents($"date=2030-01-02&venueId={venueId}");
        Assert.Equal("Alpha", Assert.Single(combined.Items).Title);
        Assert.Equal(1, combined.TotalCount);
        var searched = await ReadPublicEvents($"date=2030-01-02&venueId={venueId}&query=journey");
        Assert.Empty(searched.Items);
        Assert.Equal(0, searched.TotalCount);
        Assert.Empty((await ReadPublicEvents($"venueId={Guid.NewGuid()}")).Items);
    }

    [PostgresFact]
    public async Task PublicEvents_DateUsesUtcStartWithInclusiveLowerAndExclusiveUpperBoundary()
    {
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var venue = new VenueEntity(Guid.NewGuid(), "Venue", "Address");
            var hall = new Auditorium(Guid.NewGuid(), venue.Id, "Hall");
            db.AddRange(venue, hall);
            var day = new DateTimeOffset(2030, 1, 2, 0, 0, 0, TimeSpan.Zero);
            foreach (var (title, start) in new[]
            {
                ("Before", day.AddTicks(-10)), ("At start", day.ToOffset(TimeSpan.FromHours(2))),
                ("Before end", day.AddDays(1).AddTicks(-10)), ("At end", day.AddDays(1))
            })
            {
                var offering = new Event(Guid.NewGuid(), title, "", TimeSpan.FromMinutes(90), 0, EventStatus.Published);
                db.AddRange(offering, new Session(Guid.NewGuid(), offering.Id, hall.Id, start, start.AddMinutes(90)));
            }
            await db.SaveChangesAsync();
        }
        var page = await ReadPublicEvents("date=2030-01-02");
        Assert.Equal(new[] { "At start", "Before end" }, page.Items.Select(e => e.Title));
    }

    [PostgresTheory]
    [InlineData("%25", "100% film")]
    [InlineData("%5F", "under_score")]
    [InlineData("%5C", "back\\slash")]
    [InlineData("%27%20OR%201%3D1--", "")]
    public async Task PublicEvents_SearchEscapesWildcardsAndParameterizesText(string query, string expected)
    {
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            foreach (var title in new[] { "100% film", "under_score", "back\\slash", "ordinary" })
                db.Events.Add(new Event(Guid.NewGuid(), title, "", TimeSpan.FromMinutes(90), 0, EventStatus.Published));
            await db.SaveChangesAsync();
        }
        var page = await ReadPublicEvents($"query={query}");
        if (expected.Length == 0) Assert.Empty(page.Items);
        else Assert.Equal(expected, Assert.Single(page.Items).Title);
    }

    [PostgresFact]
    public async Task PublicEvents_PaginationUsesIdTieBreakerAndKeepsFilteredTotalOnEmptyPages()
    {
        var ids = Enumerable.Range(1, 5).Select(i => Guid.Parse($"00000000-0000-0000-0000-{i:D12}")).ToArray();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            foreach (var id in ids.Reverse()) db.Events.Add(new Event(id, "Same title", "", TimeSpan.FromMinutes(90), 0, EventStatus.Published));
            await db.SaveChangesAsync();
        }
        var first = await ReadPublicEvents("pageSize=2");
        var second = await ReadPublicEvents("page=2&pageSize=2");
        var third = await ReadPublicEvents("page=3&pageSize=2");
        var empty = await ReadPublicEvents("page=4&pageSize=2");
        Assert.Equal(ids, first.Items.Concat(second.Items).Concat(third.Items).Select(e => e.Id));
        Assert.Equal(2, second.Page);
        Assert.Equal(2, second.PageSize);
        Assert.Equal(5, empty.TotalCount);
        Assert.Empty(empty.Items);
    }

    [PostgresFact]
    public async Task PublicEvents_MaximumPageSizeIsEnforcedByTheDatabaseQuery()
    {
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            db.Events.AddRange(Enumerable.Range(0, 101).Select(i => new Event(Guid.NewGuid(), $"Film {i:D3}", "", TimeSpan.FromMinutes(90), 0, EventStatus.Published)));
            await db.SaveChangesAsync();
        }
        var first = await ReadPublicEvents("pageSize=100");
        Assert.Equal(100, first.Items.Count);
        Assert.Equal(101, first.TotalCount);
        var last = await ReadPublicEvents("page=2&pageSize=100");
        Assert.Single(last.Items);
        Assert.Equal(101, last.TotalCount);
    }

    [PostgresTheory]
    [InlineData("page=0", "page")]
    [InlineData("page=-1", "page")]
    [InlineData("page=abc", "page")]
    [InlineData("page=2147483648", "page")]
    [InlineData("page=2147483647&pageSize=100", "page")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=-1", "pageSize")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("pageSize=1.5", "pageSize")]
    [InlineData("pageSize=", "pageSize")]
    [InlineData("date=2030-02-30", "date")]
    [InlineData("date=01%2F02%2F2030", "date")]
    [InlineData("date=2030-01-02T00%3A00%3A00Z", "date")]
    [InlineData("venueId=invalid", "venueId")]
    [InlineData("venueId=00000000-0000-0000-0000-000000000000", "venueId")]
    [InlineData("venueId=", "venueId")]
    [InlineData("page=1&page=2", "page")]
    [InlineData("query=one&query=two", "query")]
    public async Task PublicEvents_InvalidInputsReturnStableFieldErrors(string query, string field)
    {
        using var response = await client.GetAsync($"/api/events?{query}");
        var problem = await AssertProblem(response, 400, "validation_failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    [PostgresFact]
    public async Task PublicEvents_QueryLengthIsBoundedAfterTrimmingAndEmptyResultsArePaged()
    {
        using var tooLong = await client.GetAsync($"/api/events?query={new string('a', 201)}");
        var problem = await AssertProblem(tooLong, 400, "validation_failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty("query", out _));
        var empty = await ReadPublicEvents($"query=%20{new string('a', 200)}%20&page=2&pageSize=1");
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.TotalCount);
        Assert.Equal(2, empty.Page);
        Assert.Equal(1, empty.PageSize);
    }

    private async Task<EventListResponse> ReadPublicEvents(string query)
    {
        using var response = await client.GetAsync($"/api/events?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<EventListResponse>())!;
    }

    private async Task<Guid> SeedPublicEvents()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var first = new VenueEntity(Guid.NewGuid(), "First", "Address");
        var second = new VenueEntity(Guid.NewGuid(), "Second", "Address");
        var firstHall = new Auditorium(Guid.NewGuid(), first.Id, "Hall");
        var secondHall = new Auditorium(Guid.NewGuid(), second.Id, "Hall");
        db.AddRange(first, second, firstHall, secondHall);
        var alpha = new Event(Guid.NewGuid(), "Alpha", "space story", TimeSpan.FromMinutes(90), 12, EventStatus.Published);
        var beta = new Event(Guid.NewGuid(), "Beta", "city journey", TimeSpan.FromMinutes(90), 12, EventStatus.Published);
        var gamma = new Event(Guid.NewGuid(), "Gamma", "SPACE documentary", TimeSpan.FromMinutes(90), 12, EventStatus.Published);
        var draft = new Event(Guid.NewGuid(), "Draft title", "space", TimeSpan.FromMinutes(90), 12);
        var archived = new Event(Guid.NewGuid(), "Archived title", "space", TimeSpan.FromMinutes(90), 12, EventStatus.Archived);
        db.AddRange(alpha, beta, gamma, draft, archived,
            new Event(Guid.NewGuid(), "Unscheduled", "", TimeSpan.FromMinutes(90), 12, EventStatus.Published));
        var day = new DateTimeOffset(2030, 1, 2, 0, 0, 0, TimeSpan.Zero);
        void AddSession(Event offering, Auditorium hall, DateTimeOffset start, SessionStatus status = SessionStatus.Scheduled) =>
            db.Sessions.Add(new Session(Guid.NewGuid(), offering.Id, hall.Id, start, start.AddMinutes(90), status));
        AddSession(alpha, firstHall, day);
        AddSession(alpha, firstHall, day.AddHours(12));
        AddSession(alpha, secondHall, day.AddDays(1));
        AddSession(beta, secondHall, day.AddHours(3));
        AddSession(beta, firstHall, day.AddDays(1));
        AddSession(gamma, firstHall, day.AddHours(4), SessionStatus.Cancelled);
        AddSession(gamma, firstHall, day.AddHours(6), SessionStatus.Completed);
        AddSession(draft, firstHall, day);
        AddSession(archived, firstHall, day);
        await db.SaveChangesAsync();
        return first.Id;
    }
}
