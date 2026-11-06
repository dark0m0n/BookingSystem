using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Administration;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresFact]
    public async Task SeatLayout_EditTrimsCoordinatesAndRetainsIdentityAndAuditorium()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var original = setup.Seats[0];
        var path = $"/api/admin/auditoriums/{setup.Hall.Id}/seats";
        // A no-op edit must not conflict with the seat itself.
        using var unchanged = await AdminRequest(HttpMethod.Patch, $"{path}/{original.Id}", token, new SeatRequest("A", 1));
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        using var updated = await AdminRequest(HttpMethod.Patch, $"{path}/{original.Id}", token,
            new { row = " B ", number = 3, type = 2, auditoriumId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var expected = original with { Row = "B", Number = 3, Type = 2 };
        Assert.Equal(expected, await updated.Content.ReadFromJsonAsync<SeatResponse>());
        using var read = await AdminRequest(HttpMethod.Get, path, token);
        Assert.Equal(new[] { setup.Seats[1], expected }, await read.Content.ReadFromJsonAsync<SeatResponse[]>());
    }

    [PostgresTheory]
    [InlineData("duplicate", 409)]
    [InlineData("blank", 400)]
    [InlineData("long-row", 400)]
    [InlineData("number", 400)]
    [InlineData("type", 400)]
    public async Task SeatLayout_RejectedEditLeavesAllSeatsUnchanged(string defect, int status)
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var input = defect switch
        {
            "duplicate" => new SeatRequest(" A ", 2, 2),
            "blank" => new SeatRequest(" ", 3),
            "long-row" => new SeatRequest(new string('B', 21), 3),
            "number" => new SeatRequest("B", 0),
            "type" => new SeatRequest("B", 3, 999),
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        var path = $"/api/admin/auditoriums/{setup.Hall.Id}/seats";
        using var response = await AdminRequest(HttpMethod.Patch, $"{path}/{setup.Seats[0].Id}", token, input);
        await AssertProblem(response, status, status == 400 ? "validation_failed" : "conflict");
        using var read = await AdminRequest(HttpMethod.Get, path, token);
        Assert.Equal(setup.Seats, await read.Content.ReadFromJsonAsync<SeatResponse[]>());
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeatLayout_ForeignSeatIsNotFoundEvenWhenTargetLayoutIsFixed(bool fixedLayout)
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var other = await CreateAdmin<AuditoriumResponse>("/api/admin/auditoriums", token,
            new CreateAuditoriumRequest(setup.Venue.Id, "Other"));
        var foreign = await CreateAdmin<SeatResponse[]>($"/api/admin/auditoriums/{other.Id}/seats", token,
            new AddSeatsRequest([new("A", 1)]));
        if (fixedLayout) await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));

        foreach (var auditoriumId in new[] { setup.Hall.Id, Guid.NewGuid() })
        {
            var path = $"/api/admin/auditoriums/{auditoriumId}/seats/{foreign[0].Id}";
            using var edit = await AdminRequest(HttpMethod.Patch, path, token, new SeatRequest("B", 2));
            await AssertProblem(edit, 404, "not_found");
            using var delete = await AdminRequest(HttpMethod.Delete, path, token);
            await AssertProblem(delete, 404, "not_found");
        }
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var stored = await db.Seats.SingleAsync(s => s.Id == foreign[0].Id);
        Assert.Equal(other.Id, stored.AuditoriumId);
        Assert.Equal("A", stored.Row);
        Assert.Equal(1, stored.Number);
        Assert.Equal(setup.Seats.Length + 1, await db.Seats.CountAsync());
    }

    [PostgresFact]
    public async Task SeatLayout_EditRacingSessionCreationPreservesTheCapturedLayout()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var operations = await Task.WhenAll(
            AdminRequest(HttpMethod.Patch, $"/api/admin/auditoriums/{setup.Hall.Id}/seats/{setup.Seats[0].Id}", token, new SeatRequest("B", 3, 2)),
            AdminRequest(HttpMethod.Post, "/api/admin/sessions", token, SessionRequest(setup)));
        using var edit = operations[0];
        using var created = operations[1];
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Contains(edit.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        var session = (await created.Content.ReadFromJsonAsync<SessionResponse>())!;
        var captured = Assert.Single(session.Seats, s => s.SeatId == setup.Seats[0].Id);
        Assert.Equal(edit.StatusCode == HttpStatusCode.OK ? "B" : "A", captured.Row);
        Assert.Equal(edit.StatusCode == HttpStatusCode.OK ? 3 : 1, captured.Number);
        Assert.Equal(edit.StatusCode == HttpStatusCode.OK ? 2 : 0, captured.Type);
        using var read = await AdminRequest(HttpMethod.Get, $"/api/admin/sessions/{session.Id}", token);
        Assert.Equal(session.Seats, (await read.Content.ReadFromJsonAsync<SessionResponse>())!.Seats);
    }

    [PostgresFact]
    public async Task SeatLayout_ConcurrentEditsToSameCoordinatesAllowOnlyOneChange()
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var path = $"/api/admin/auditoriums/{setup.Hall.Id}/seats";
        var operations = await Task.WhenAll(setup.Seats.Select(s =>
            AdminRequest(HttpMethod.Patch, $"{path}/{s.Id}", token, new SeatRequest("B", 1))));
        try
        {
            Assert.Single(operations, r => r.StatusCode == HttpStatusCode.OK);
            await AssertProblem(Assert.Single(operations, r => r.StatusCode == HttpStatusCode.Conflict), 409, "conflict");
            await using var scope = app.Services.CreateAsyncScope();
            var seats = await scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().Seats.ToListAsync();
            Assert.Equal(2, seats.Count);
            Assert.Single(seats, s => s.Row == "B" && s.Number == 1);
            Assert.Single(seats, s => s.Row == "A");
        }
        finally
        {
            foreach (var response in operations) response.Dispose();
        }
    }
}
