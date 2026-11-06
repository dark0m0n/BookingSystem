using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Administration;
using TicketBooking.Contracts.Catalogue;
using TicketBooking.Domain;
using TicketBooking.Domain.Booking;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;
using TicketBooking.Domain.Venue;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;
using VenueEntity = TicketBooking.Domain.Venue.Venue;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresFact]
    public async Task PublicDetails_AnonymousBrowsingReturnsMetadataAndCompleteLiveSeatMapWithoutOwners()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        await CreateAdmin<SeatResponse[]>($"/api/admin/auditoriums/{setup.Hall.Id}/seats", token,
            new AddSeatsRequest([new("B", 1, 2), new("B", 2)]));
        var session = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup) with { Price = 125.50m, Currency = "eur" });
        await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup) with { Price = 75m });
        var ownerId = Guid.NewGuid();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var writer = new SessionSeatAllocationWriter(db);
            await writer.HoldAsync(session.Seats[1].Id, 0);
            await writer.HoldAsync(session.Seats[2].Id, 0);
            await writer.ConfirmAsync(session.Seats[2].Id, 1);
            await writer.HoldAsync(session.Seats[3].Id, 0);
            await writer.ReleaseAsync(session.Seats[3].Id, 1, SessionSeatStatus.Held, SessionSeatStatus.Unavailable);
            var available = await db.SessionSeats.SingleAsync(s => s.Id == session.Seats[0].Id);
            var timing = EditProtectionTiming();
            var hold = new SeatHold(Guid.NewGuid(), session.Id, ownerId, session.StartsAt, [available], clock, timing);
            db.SeatHolds.Add(hold);
            db.Bookings.Add(Booking.CreateFromHold(Guid.NewGuid(), hold, ownerId, "BK-PRIVATE", clock, session.StartsAt, timing));
            await db.SaveChangesAsync();
        }

        using var eventRead = await client.GetAsync($"/api/events/{setup.Event.Id}");
        Assert.Equal(HttpStatusCode.OK, eventRead.StatusCode);
        Assert.Equal(new EventSummaryResponse(setup.Event.Id, setup.Event.Title, setup.Event.Description, 90, 12),
            await eventRead.Content.ReadFromJsonAsync<EventSummaryResponse>());
        using var sessionRead = await client.GetAsync($"/api/sessions/{session.Id}");
        Assert.Equal(HttpStatusCode.OK, sessionRead.StatusCode);
        var detail = (await sessionRead.Content.ReadFromJsonAsync<SessionDetailsResponse>())!;
        Assert.Equal(session.Id, detail.Id);
        Assert.Equal(setup.Event.Id, detail.EventId);
        Assert.Equal(setup.Event.Title, detail.EventTitle);
        Assert.Equal(setup.Venue.Id, detail.VenueId);
        Assert.Equal(setup.Venue.Name, detail.VenueName);
        Assert.Equal(setup.Venue.Address, detail.VenueAddress);
        Assert.Equal(setup.Hall.Id, detail.AuditoriumId);
        Assert.Equal(setup.Hall.Name, detail.AuditoriumName);
        Assert.Equal(session.StartsAt, detail.StartsAt);
        Assert.Equal(session.EndsAt, detail.EndsAt);
        Assert.Equal(TimeSpan.Zero, detail.StartsAt.Offset);
        Assert.Equal(0, detail.Status);

        using var sessionsRead = await client.GetAsync($"/api/events/{setup.Event.Id}/sessions");
        Assert.Equal(HttpStatusCode.OK, sessionsRead.StatusCode);
        var page = (await sessionsRead.Content.ReadFromJsonAsync<EventSessionListResponse>())!;
        Assert.Equal(2, page.TotalCount);
        Assert.Contains(detail, page.Items);
        using var mapRead = await client.GetAsync($"/api/sessions/{session.Id}/seats");
        Assert.Equal(HttpStatusCode.OK, mapRead.StatusCode);
        Assert.True(mapRead.Headers.CacheControl?.NoStore);
        var map = (await mapRead.Content.ReadFromJsonAsync<SessionSeatMapResponse>())!;
        Assert.Equal(session.Id, map.SessionId);
        Assert.Equal(setup.Hall.Id, map.AuditoriumId);
        Assert.Equal(session.Seats.Select(s => s.Id), map.Seats.Select(s => s.Id));
        Assert.Equal(session.Seats.Select(s => s.SeatId), map.Seats.Select(s => s.SeatId));
        Assert.Equal(new[] { "A", "A", "B", "B" }, map.Seats.Select(s => s.Row));
        Assert.Equal(new[] { 1, 2, 1, 2 }, map.Seats.Select(s => s.Number));
        Assert.Equal(new[] { 0, 1, 2, 0 }, map.Seats.Select(s => s.Type));
        Assert.Equal(new[] { 0, 1, 2, 3 }, map.Seats.Select(s => s.Status));
        Assert.Equal(new long[] { 0, 1, 2, 2 }, map.Seats.Select(s => s.Version));
        Assert.All(map.Seats, s => { Assert.Equal(125.50m, s.Price); Assert.Equal("EUR", s.Currency); });
        var body = await mapRead.Content.ReadAsStringAsync();
        foreach (var hidden in new[] { ownerId.ToString(), "userId", "ownerId", "holdId", "bookingId", "BK-PRIVATE", "expiresAt" })
            Assert.DoesNotContain(hidden, body, StringComparison.OrdinalIgnoreCase);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            await new SessionSeatAllocationWriter(scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>())
                .ReleaseAsync(session.Seats[1].Id, 1, SessionSeatStatus.Held, SessionSeatStatus.Available);
        }
        using var refreshed = await client.GetAsync($"/api/sessions/{session.Id}/seats");
        var freshSeat = (await refreshed.Content.ReadFromJsonAsync<SessionSeatMapResponse>())!.Seats[1];
        Assert.Equal(0, freshSeat.Status);
        Assert.Equal(2, freshSeat.Version);
    }

    [PostgresTheory]
    [InlineData(EventStatus.Draft, SessionStatus.Scheduled)]
    [InlineData(EventStatus.Archived, SessionStatus.Scheduled)]
    [InlineData(EventStatus.Published, SessionStatus.Cancelled)]
    [InlineData(EventStatus.Published, SessionStatus.Completed)]
    public async Task PublicDetails_HiddenEventsAndSessionsAreNotExposed(EventStatus eventStatus, SessionStatus sessionStatus)
    {
        Guid eventId, sessionId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var venue = new VenueEntity(Guid.NewGuid(), "Venue", "Address");
            var hall = new Auditorium(Guid.NewGuid(), venue.Id, "Hall");
            var offering = new Event(Guid.NewGuid(), "Private event", "", TimeSpan.FromMinutes(90), 0, eventStatus);
            var start = clock.GetUtcNow().AddHours(2);
            var session = new Session(Guid.NewGuid(), offering.Id, hall.Id, start, start.AddMinutes(90), sessionStatus);
            eventId = offering.Id; sessionId = session.Id;
            db.AddRange(venue, hall, offering, session);
            await db.SaveChangesAsync();
        }
        foreach (var suffix in new[] { "", "/sessions" })
        {
            using var response = await client.GetAsync($"/api/events/{eventId}{suffix}");
            if (eventStatus != EventStatus.Published) await AssertProblem(response, 404, "not_found");
            else
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                if (suffix.Length > 0) Assert.Empty((await response.Content.ReadFromJsonAsync<EventSessionListResponse>())!.Items);
            }
        }
        foreach (var suffix in new[] { "", "/seats" })
        {
            using var response = await client.GetAsync($"/api/sessions/{sessionId}{suffix}");
            await AssertProblem(response, 404, "not_found");
        }
    }

    [PostgresTheory]
    [InlineData("/api/events/{0}")]
    [InlineData("/api/events/{0}/sessions")]
    [InlineData("/api/sessions/{0}")]
    [InlineData("/api/sessions/{0}/seats")]
    public async Task PublicDetails_MissingEmptyOrMalformedIdsUseNotFound(string route)
    {
        foreach (var id in new[] { Guid.NewGuid().ToString(), Guid.Empty.ToString(), "invalid" })
        {
            using var response = await client.GetAsync(string.Format(route, id));
            await AssertProblem(response, 404, "not_found");
        }
    }

    [PostgresFact]
    public async Task PublicEventSessions_FiltersDateAndVenueAndPaginatesInTimeOrder()
    {
        var venue = await SeedPublicEvents();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var id = await db.Events.Where(e => e.Title == "Alpha").Select(e => e.Id).SingleAsync();
        var all = await ReadEventSessions(id);
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(all.Items.OrderBy(s => s.StartsAt).ThenBy(s => s.Id), all.Items);
        var filtered = await ReadEventSessions(id, $"date=2030-01-02&venueId={venue}&pageSize=1&page=2");
        Assert.Equal(2, filtered.TotalCount);
        Assert.Equal(all.Items[1], Assert.Single(filtered.Items));
        Assert.Equal(2, filtered.Page);
        Assert.Equal(1, filtered.PageSize);
        Assert.Empty((await ReadEventSessions(id, $"venueId={Guid.NewGuid()}")).Items);
        Assert.Empty((await ReadEventSessions(id, "date=9999-12-31")).Items);
        Assert.Empty((await ReadEventSessions(id, "page=10")).Items);
        var unscheduled = await db.Events.Where(e => e.Title == "Unscheduled").Select(e => e.Id).SingleAsync();
        var empty = await ReadEventSessions(unscheduled);
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.TotalCount);
    }

    [PostgresTheory]
    [InlineData("page=0", "page")]
    [InlineData("page=2147483647&pageSize=100", "page")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("pageSize=oops", "pageSize")]
    [InlineData("date=2030-02-30", "date")]
    [InlineData("venueId=", "venueId")]
    [InlineData("date=2030-01-02&date=2030-01-03", "date")]
    public async Task PublicEventSessions_RejectsMalformedOrUnboundedFilters(string query, string field)
    {
        using var response = await client.GetAsync($"/api/events/{Guid.NewGuid()}/sessions?{query}");
        var problem = await AssertProblem(response, 400, "validation_failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    [PostgresFact]
    public async Task PublicEventSessions_MaximumPageSizeAndTimeTieBreakerAreStable()
    {
        Guid eventId;
        var ids = Enumerable.Range(1, 101).Select(i => Guid.Parse($"00000000-0000-0000-0000-{i:D12}")).ToArray();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var venue = new VenueEntity(Guid.NewGuid(), "Venue", "Address");
            var hall = new Auditorium(Guid.NewGuid(), venue.Id, "Hall");
            var offering = new Event(Guid.NewGuid(), "Film", "", TimeSpan.FromMinutes(90), 0, EventStatus.Published);
            eventId = offering.Id;
            db.AddRange(venue, hall, offering);
            var start = clock.GetUtcNow().AddHours(2);
            db.Sessions.AddRange(ids.Reverse().Select(id => new Session(id, offering.Id, hall.Id, start, start.AddMinutes(90))));
            await db.SaveChangesAsync();
        }
        var first = await ReadEventSessions(eventId, "pageSize=100");
        var last = await ReadEventSessions(eventId, "pageSize=100&page=2");
        Assert.Equal(101, first.TotalCount);
        Assert.Equal(100, first.Items.Count);
        Assert.Single(last.Items);
        Assert.Equal(ids, first.Items.Concat(last.Items).Select(s => s.Id));
    }

    [PostgresTheory]
    [InlineData("missing")]
    [InlineData("foreign-extra")]
    [InlineData("foreign-replacement")]
    public async Task PublicSeatMap_InconsistentInventoryReturnsConflictWithoutForeignSeatData(string defect)
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var session = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            if (defect != "foreign-extra") db.SessionSeats.Remove(await db.SessionSeats.FirstAsync(s => s.SessionId == session.Id));
            if (defect != "missing")
            {
                var hall = new Auditorium(Guid.NewGuid(), setup.Venue.Id, "Other");
                var seat = new Seat(Guid.NewGuid(), hall.Id, "PRIVATE-ROW", 1);
                db.AddRange(hall, seat, new SessionSeat(Guid.NewGuid(), session.Id, seat.Id, new Money(777m, "USD")));
            }
            await db.SaveChangesAsync();
        }
        using var response = await client.GetAsync($"/api/sessions/{session.Id}/seats");
        await AssertProblem(response, 409, "conflict");
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("PRIVATE-ROW", body);
        Assert.DoesNotContain("\"price\"", body);
    }

    [PostgresFact]
    public async Task PublicSeatMap_RacingAuditoriumMoveReturnsOneCompleteLayoutSnapshot()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var session = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        var hall = await CreateAdmin<AuditoriumResponse>("/api/admin/auditoriums", token, new CreateAuditoriumRequest(setup.Venue.Id, "Destination"));
        var seats = await CreateAdmin<SeatResponse[]>($"/api/admin/auditoriums/{hall.Id}/seats", token,
            new AddSeatsRequest([new("X", 1), new("X", 2), new("X", 3)]));
        var responses = await Task.WhenAll(client.GetAsync($"/api/sessions/{session.Id}/seats"),
            AdminRequest(HttpMethod.Patch, $"/api/admin/sessions/{session.Id}", token, new UpdateSessionRequest(AuditoriumId: hall.Id)));
        using var read = responses[0];
        using var moved = responses[1];
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var map = (await read.Content.ReadFromJsonAsync<SessionSeatMapResponse>())!;
        var expected = map.AuditoriumId == setup.Hall.Id ? setup.Seats : seats;
        Assert.Equal(expected.Select(s => s.Id).Order(), map.Seats.Select(s => s.SeatId).Order());
        Assert.All(map.Seats, s => Assert.Equal(100m, s.Price));
    }

    private async Task<EventSessionListResponse> ReadEventSessions(Guid eventId, string query = "")
    {
        using var response = await client.GetAsync($"/api/events/{eventId}/sessions?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<EventSessionListResponse>())!;
    }
}
