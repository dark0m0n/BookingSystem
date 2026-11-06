using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Administration;
using TicketBooking.Domain;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresFact]
    public async Task AdminSession_ReschedulesInUtcKeepsCapturedDurationAndFencesStaleInventory()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var original = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        using var eventEdit = await AdminRequest(HttpMethod.Patch, $"/api/admin/events/{setup.Event.Id}", token, new UpdateEventRequest(DurationMinutes: 120));
        Assert.Equal(HttpStatusCode.OK, eventEdit.StatusCode);
        var start = clock.GetUtcNow().AddHours(4).ToOffset(TimeSpan.FromHours(2));
        using var edit = await AdminRequest(HttpMethod.Patch, $"/api/admin/sessions/{original.Id}", token,
            new { startsAt = start, endsAt = start.AddMinutes(1), status = 2, price = 1m });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var updated = (await edit.Content.ReadFromJsonAsync<SessionResponse>())!;
        Assert.Equal(original.Id, updated.Id);
        Assert.Equal(new DateTimeOffset(start.UtcTicks / 10 * 10, TimeSpan.Zero), updated.StartsAt);
        Assert.Equal(TimeSpan.Zero, updated.StartsAt.Offset);
        Assert.Equal(updated.StartsAt.AddMinutes(90), updated.EndsAt);
        Assert.Equal(original.Seats.Select(s => s with { Version = 1 }), updated.Seats);
        Assert.Equal(0, updated.Status);
        await using var scope = app.Services.CreateAsyncScope();
        var writer = new SessionSeatAllocationWriter(scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>());
        await Assert.ThrowsAsync<SeatAllocationConflictException>(() => writer.HoldAsync(original.Seats[0].Id, 0));
    }

    [PostgresFact]
    public async Task AdminSession_MoveAndChangeEventRebuildsExactlyTheDestinationInventory()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var original = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        var venue = await CreateAdmin<VenueResponse>("/api/admin/venues", token, new CreateVenueRequest("Other venue", "Address"));
        var hall = await CreateAdmin<AuditoriumResponse>("/api/admin/auditoriums", token, new CreateAuditoriumRequest(venue.Id, "Other hall"));
        var seats = await CreateAdmin<SeatResponse[]>($"/api/admin/auditoriums/{hall.Id}/seats", token, new AddSeatsRequest([new("X", 1, 2)]));
        var offering = await CreateAdmin<EventResponse>("/api/admin/events", token, new CreateEventRequest("Other film", "", 120, 0, 1));
        using var edit = await AdminRequest(HttpMethod.Patch, $"/api/admin/sessions/{original.Id}", token,
            new UpdateSessionRequest(offering.Id, hall.Id));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var updated = (await edit.Content.ReadFromJsonAsync<SessionResponse>())!;
        Assert.Equal(original.Id, updated.Id);
        Assert.Equal(hall.Id, updated.AuditoriumId);
        Assert.Equal(offering.Id, updated.EventId);
        Assert.Equal(original.StartsAt, updated.StartsAt);
        Assert.Equal(updated.StartsAt.AddMinutes(120), updated.EndsAt);
        var inventory = Assert.Single(updated.Seats);
        Assert.Equal(seats[0].Id, inventory.SeatId);
        Assert.Equal("X", inventory.Row);
        Assert.Equal(2, inventory.Type);
        Assert.Equal(100m, inventory.Price);
        Assert.Equal("UAH", inventory.Currency);
        Assert.Equal(0, inventory.Version);
        using var read = await AdminRequest(HttpMethod.Get, $"/api/admin/sessions/{original.Id}", token);
        Assert.Equal(updated.Seats, (await read.Content.ReadFromJsonAsync<SessionResponse>())!.Seats);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Equal(1, await db.SessionSeats.CountAsync());
        var writer = new SessionSeatAllocationWriter(db);
        await Assert.ThrowsAsync<SeatAllocationConflictException>(() => writer.HoldAsync(original.Seats[0].Id, 0));
    }

    [PostgresTheory]
    [InlineData("empty", 400)]
    [InlineData("empty-event", 400)]
    [InlineData("empty-hall", 400)]
    [InlineData("missing-event", 404)]
    [InlineData("missing-hall", 404)]
    [InlineData("empty-layout", 409)]
    [InlineData("draft", 409)]
    [InlineData("now", 400)]
    [InlineData("past-tick", 400)]
    [InlineData("future-tick", 400)]
    [InlineData("overflow", 400)]
    public async Task AdminSession_InvalidUpdateRetainsScheduleAndInventory(string defect, int status)
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var original = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        // PostgreSQL stores microseconds. One future tick in the same microsecond is still not future.
        clock.Set(new DateTimeOffset(clock.GetUtcNow().UtcTicks / 10 * 10, TimeSpan.Zero));
        var input = defect switch
        {
            "empty" => new UpdateSessionRequest(),
            "empty-event" => new UpdateSessionRequest(EventId: Guid.Empty),
            "empty-hall" => new UpdateSessionRequest(AuditoriumId: Guid.Empty),
            "missing-event" => new UpdateSessionRequest(EventId: Guid.NewGuid()),
            "missing-hall" => new UpdateSessionRequest(AuditoriumId: Guid.NewGuid()),
            "empty-layout" => new UpdateSessionRequest(AuditoriumId: (await CreateAdmin<AuditoriumResponse>("/api/admin/auditoriums", token,
                new CreateAuditoriumRequest(setup.Venue.Id, "Empty"))).Id),
            "draft" => new UpdateSessionRequest(EventId: (await CreateAdmin<EventResponse>("/api/admin/events", token,
                new CreateEventRequest("Draft", "", 90, 0))).Id),
            "now" => new UpdateSessionRequest(StartsAt: clock.GetUtcNow()),
            "past-tick" => new UpdateSessionRequest(StartsAt: clock.GetUtcNow().AddTicks(-1)),
            "future-tick" => new UpdateSessionRequest(StartsAt: clock.GetUtcNow().AddTicks(1)),
            "overflow" => new UpdateSessionRequest(StartsAt: DateTimeOffset.MaxValue),
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        using var response = await AdminRequest(HttpMethod.Patch, $"/api/admin/sessions/{original.Id}", token, input);
        await AssertProblem(response, status, status == 400 ? "validation_failed" : status == 404 ? "not_found" : "conflict");
        using var read = await AdminRequest(HttpMethod.Get, $"/api/admin/sessions/{original.Id}", token);
        var stored = (await read.Content.ReadFromJsonAsync<SessionResponse>())!;
        Assert.Equal(original.StartsAt, stored.StartsAt);
        Assert.Equal(original.EndsAt, stored.EndsAt);
        Assert.Equal(original.EventId, stored.EventId);
        Assert.Equal(original.AuditoriumId, stored.AuditoriumId);
        Assert.Equal(original.Seats, stored.Seats);
    }

    [PostgresTheory]
    [InlineData("allocated")]
    [InlineData("history")]
    [InlineData("booking")]
    [InlineData("started")]
    public async Task AdminSession_IneligibleSessionCannotBeRescheduled(string reason)
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var original = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            if (reason == "allocated") await new SessionSeatAllocationWriter(db).HoldAsync(original.Seats[0].Id, 0);
            if (reason is "history" or "booking")
            {
                var seat = await db.SessionSeats.FirstAsync();
                var timing = new BookingTimingPolicy(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1));
                var hold = new SeatHold(Guid.NewGuid(), original.Id, Guid.NewGuid(), original.StartsAt, [seat], clock, timing);
                if (reason == "history") db.SeatHolds.Add(hold);
                else db.Bookings.Add(TicketBooking.Domain.Booking.Booking.CreateFromHold(Guid.NewGuid(), hold, hold.UserId,
                    "BK-SESSION-TEST", clock, original.StartsAt, timing));
                await db.SaveChangesAsync();
            }
        }
        if (reason == "started")
        {
            clock.Set(original.StartsAt);
            token = (await LoginToken()).AccessToken;
        }
        using var response = await AdminRequest(HttpMethod.Patch, $"/api/admin/sessions/{original.Id}", token,
            new UpdateSessionRequest(StartsAt: clock.GetUtcNow().AddHours(5)));
        await AssertProblem(response, 409, "conflict");
    }

    [PostgresFact]
    public async Task AdminSession_FailedMoveRollsBackScheduleInventoryAndVersions()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var original = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        var hall = await CreateAdmin<AuditoriumResponse>("/api/admin/auditoriums", token, new CreateAuditoriumRequest(setup.Venue.Id, "Other"));
        await CreateAdmin<SeatResponse[]>($"/api/admin/auditoriums/{hall.Id}/seats", token, new AddSeatsRequest([new("X", 1)]));
        adminInventoryWriteFailure.FailNext();
        using var response = await AdminRequest(HttpMethod.Patch, $"/api/admin/sessions/{original.Id}", token, new UpdateSessionRequest(AuditoriumId: hall.Id));
        await AssertProblem(response, 500, "internal_error");
        using var read = await AdminRequest(HttpMethod.Get, $"/api/admin/sessions/{original.Id}", token);
        var stored = (await read.Content.ReadFromJsonAsync<SessionResponse>())!;
        Assert.Equal(original.AuditoriumId, stored.AuditoriumId);
        Assert.Equal(original.Seats, stored.Seats);
        using var retry = await AdminRequest(HttpMethod.Patch, $"/api/admin/sessions/{original.Id}", token, new UpdateSessionRequest(AuditoriumId: hall.Id));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [PostgresFact]
    public async Task AdminSession_MissingSessionIsNotFoundAndOneFutureMicrosecondIsAccepted()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var original = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        clock.Set(new DateTimeOffset(clock.GetUtcNow().UtcTicks / 10 * 10, TimeSpan.Zero));
        var input = new UpdateSessionRequest(StartsAt: clock.GetUtcNow().AddTicks(10));
        using var missing = await AdminRequest(HttpMethod.Patch, $"/api/admin/sessions/{Guid.NewGuid()}", token, input);
        await AssertProblem(missing, 404, "not_found");
        using var accepted = await AdminRequest(HttpMethod.Patch, $"/api/admin/sessions/{original.Id}", token, input);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(input.StartsAt, (await accepted.Content.ReadFromJsonAsync<SessionResponse>())!.StartsAt);
    }

    [PostgresTheory]
    [InlineData(SessionStatus.Cancelled)]
    [InlineData(SessionStatus.Completed)]
    public async Task AdminSession_NonScheduledSessionIsNotEditable(SessionStatus status)
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var original = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Sessions\" SET \"Status\" = {(int)status} WHERE \"Id\" = {original.Id}");
        }
        using var edit = await AdminRequest(HttpMethod.Patch, $"/api/admin/sessions/{original.Id}", token,
            new UpdateSessionRequest(StartsAt: clock.GetUtcNow().AddHours(4)));
        await AssertProblem(edit, 409, "conflict");
    }

    [PostgresFact]
    public async Task AdminSession_MoveRacingDestinationLayoutEditReturnsACompleteMap()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var original = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        var hall = await CreateAdmin<AuditoriumResponse>("/api/admin/auditoriums", token, new CreateAuditoriumRequest(setup.Venue.Id, "Other"));
        await CreateAdmin<SeatResponse[]>($"/api/admin/auditoriums/{hall.Id}/seats", token, new AddSeatsRequest([new("X", 1)]));
        var responses = await Task.WhenAll(
            AdminRequest(HttpMethod.Patch, $"/api/admin/sessions/{original.Id}", token, new UpdateSessionRequest(AuditoriumId: hall.Id)),
            AdminRequest(HttpMethod.Post, $"/api/admin/auditoriums/{hall.Id}/seats", token, new AddSeatsRequest([new("X", 2)])));
        using var moved = responses[0];
        using var added = responses[1];
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Contains(added.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });
        var session = (await moved.Content.ReadFromJsonAsync<SessionResponse>())!;
        Assert.Equal(added.StatusCode == HttpStatusCode.Created ? 2 : 1, session.Seats.Count);
        using var read = await AdminRequest(HttpMethod.Get, $"/api/admin/auditoriums/{hall.Id}/seats", token);
        var seats = (await read.Content.ReadFromJsonAsync<SeatResponse[]>())!;
        Assert.Equal(seats.Select(s => s.Id).Order(), session.Seats.Select(s => s.SeatId).Order());
    }

    [PostgresFact]
    public async Task AdminSession_RescheduleRacingAllocationFencesOneOperation()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var original = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        var editTask = AdminRequest(HttpMethod.Patch, $"/api/admin/sessions/{original.Id}", token,
            new UpdateSessionRequest(StartsAt: clock.GetUtcNow().AddHours(4)));
        await using var scope = app.Services.CreateAsyncScope();
        var writer = new SessionSeatAllocationWriter(scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>());
        var allocationError = await Record.ExceptionAsync(() => writer.HoldAsync(original.Seats[0].Id, 0));
        using var edited = await editTask;
        if (allocationError is null) await AssertProblem(edited, 409, "conflict");
        else
        {
            Assert.IsType<SeatAllocationConflictException>(allocationError);
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        }
    }
}
