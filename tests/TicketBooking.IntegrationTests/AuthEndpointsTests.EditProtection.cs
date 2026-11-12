using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Administration;
using TicketBooking.Domain;
using TicketBooking.Domain.Booking;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Persistence;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresTheory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    [InlineData(true, 4)]
    public async Task AdminEdits_AnyReservationHistoryRejectsDestructiveEditsWithoutChangingSnapshots(bool booking, int status)
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var session = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        var other = await CreateEditDestination(token, setup);
        await SeedEditHistory(session, booking, status);
        var before = await CaptureEditState();
        var hallPath = $"/api/admin/auditoriums/{setup.Hall.Id}";
        var sessionPath = $"/api/admin/sessions/{session.Id}";
        var attempts = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Post, $"{hallPath}/seats", new AddSeatsRequest([new("B", 1)])),
            (HttpMethod.Patch, $"{hallPath}/seats/{setup.Seats[0].Id}", new SeatRequest("B", 3, 2)),
            (HttpMethod.Delete, $"{hallPath}/seats/{setup.Seats[0].Id}", null),
            (HttpMethod.Delete, hallPath, null),
            (HttpMethod.Patch, sessionPath, new UpdateSessionRequest(StartsAt: clock.GetUtcNow().AddHours(4))),
            (HttpMethod.Patch, sessionPath, new UpdateSessionRequest(EventId: other.Event.Id)),
            (HttpMethod.Patch, sessionPath, new UpdateSessionRequest(AuditoriumId: other.Hall.Id))
        };
        foreach (var (method, path, body) in attempts)
        {
            using var response = await AdminRequest(method, path, token, body);
            await AssertProblem(response, 409, "conflict");
            Assert.Equal(before, await CaptureEditState());
        }
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdminSession_HistoryItemsBlockEditsEvenWhenHeaderNamesAnotherSession(bool booking)
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var session = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        var otherSession = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        var destination = await CreateEditDestination(token, setup);
        // Simulate imported/inconsistent data permitted by the independent foreign keys.
        await SeedEditHistory(session, booking, 0, headerSessionId: otherSession.Id);
        var before = await CaptureEditState();
        foreach (var input in new[]
        {
            new UpdateSessionRequest(StartsAt: clock.GetUtcNow().AddHours(4)),
            new UpdateSessionRequest(AuditoriumId: destination.Hall.Id),
            new UpdateSessionRequest(EventId: destination.Event.Id)
        })
        {
            using var response = await AdminRequest(HttpMethod.Patch, $"/api/admin/sessions/{session.Id}", token, input);
            await AssertProblem(response, 409, "conflict");
            Assert.Equal(before, await CaptureEditState());
        }
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdminLayout_InventoryReferencesBlockEditsEvenWhenSessionNamesAnotherAuditorium(bool booking)
    {
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var session = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        var destination = await CreateEditDestination(token, setup);
        Guid inventoryId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var physical = await db.Seats.SingleAsync(s => s.AuditoriumId == destination.Hall.Id);
            var inventory = new SessionSeat(Guid.NewGuid(), session.Id, physical.Id, new Money(100m, "UAH"));
            inventoryId = inventory.Id;
            db.SessionSeats.Add(inventory);
            await db.SaveChangesAsync();
        }
        await SeedEditHistory(session, booking, 0, inventoryId: inventoryId);
        var before = await CaptureEditState();
        var hallPath = $"/api/admin/auditoriums/{destination.Hall.Id}";
        await using var readScope = app.Services.CreateAsyncScope();
        var seatId = await readScope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().Seats
            .Where(s => s.AuditoriumId == destination.Hall.Id).Select(s => s.Id).SingleAsync();
        var attempts = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Post, $"{hallPath}/seats", new AddSeatsRequest([new("Y", 1)])),
            (HttpMethod.Patch, $"{hallPath}/seats/{seatId}", new SeatRequest("Y", 2, 2)),
            (HttpMethod.Delete, $"{hallPath}/seats/{seatId}", null),
            (HttpMethod.Delete, hallPath, null)
        };
        foreach (var (method, path, body) in attempts)
        {
            using var response = await AdminRequest(method, path, token, body);
            await AssertProblem(response, 409, "conflict");
            Assert.Equal(before, await CaptureEditState());
        }
    }

    [PostgresTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AdminSession_EditRacingReservationCommitPreservesTheWinningTransaction(bool booking, bool move)
    {
        // Match PostgreSQL timestamp precision for exact captured-deadline assertions.
        clock.Set(new DateTimeOffset(clock.GetUtcNow().UtcTicks / 10 * 10, TimeSpan.Zero));
        var token = await AdminToken();
        var setup = await CreateAdminLayout(token);
        var session = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(setup));
        var destination = await CreateEditDestination(token, setup);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var seat = await db.SessionSeats.AsNoTracking().FirstAsync(s => s.SessionId == session.Id);
        var timing = EditProtectionTiming();
        var hold = new SeatHold(Guid.NewGuid(), session.Id, Guid.NewGuid(), session.StartsAt, [seat], clock, timing);
        var pendingBooking = booking ? Booking.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "BK-RACE", clock, session.StartsAt, timing) : null;
        var input = move ? new UpdateSessionRequest(AuditoriumId: destination.Hall.Id) : new UpdateSessionRequest(StartsAt: clock.GetUtcNow().AddHours(4));
        var edit = AdminRequest(HttpMethod.Patch, $"/api/admin/sessions/{session.Id}", token, input);
        var reservationFailure = await Record.ExceptionAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await new SessionSeatAllocationWriter(db).HoldAsync(seat.Id, seat.Version);
            db.SeatHolds.Add(hold);
            if (booking) db.Bookings.Add(pendingBooking!);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        });
        using var result = await edit;
        db.ChangeTracker.Clear();
        var stored = await db.Sessions.AsNoTracking().SingleAsync(s => s.Id == session.Id);
        if (reservationFailure is null)
        {
            await AssertProblem(result, 409, "conflict");
            Assert.Equal(session.StartsAt, stored.StartsAt);
            Assert.Equal(session.AuditoriumId, stored.AuditoriumId);
            Assert.Equal(session.EventId, stored.EventId);
            Assert.Equal(SessionSeatStatus.Held, (await db.SessionSeats.SingleAsync(s => s.Id == seat.Id)).Status);
            if (booking)
            {
                var saved = await db.Bookings.SingleAsync();
                Assert.Equal(pendingBooking!.PaymentDeadline, saved.PaymentDeadline);
                Assert.Equal(pendingBooking.CancellationDeadline, saved.CancellationDeadline);
                Assert.Equal(pendingBooking.TotalAmount, saved.TotalAmount);
            }
            else Assert.Equal(hold.ExpiresAt, (await db.SeatHolds.SingleAsync()).ExpiresAt);
        }
        else
        {
            Assert.IsType<SeatAllocationConflictException>(reservationFailure);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Empty(await db.SeatHolds.ToArrayAsync());
            Assert.Empty(await db.Bookings.ToArrayAsync());
            Assert.Equal(move ? destination.Hall.Id : session.AuditoriumId, stored.AuditoriumId);
            Assert.True(move || stored.StartsAt > session.StartsAt);
        }
    }

    private static BookingTimingPolicy EditProtectionTiming() =>
        new(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1));

    private async Task<(AuditoriumResponse Hall, EventResponse Event)> CreateEditDestination(string token, AdminLayout setup)
    {
        var hall = await CreateAdmin<AuditoriumResponse>("/api/admin/auditoriums", token, new CreateAuditoriumRequest(setup.Venue.Id, "Destination"));
        await CreateAdmin<SeatResponse[]>($"/api/admin/auditoriums/{hall.Id}/seats", token, new AddSeatsRequest([new("X", 1)]));
        var offering = await CreateAdmin<EventResponse>("/api/admin/events", token, new CreateEventRequest("Replacement", "", 120, 0, 1));
        return (hall, offering);
    }

    private async Task SeedEditHistory(SessionResponse session, bool booking, int status, Guid? headerSessionId = null, Guid? inventoryId = null)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var seat = await db.SessionSeats.FirstAsync(s => s.SessionId == session.Id && (inventoryId == null || s.Id == inventoryId));
        var timing = EditProtectionTiming();
        var hold = new SeatHold(Guid.NewGuid(), session.Id, Guid.NewGuid(), session.StartsAt, [seat], clock, timing);
        if (booking)
        {
            db.SeatHolds.Add(hold);
            var saved = Booking.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "BK-PROTECTED", clock, session.StartsAt, timing);
            db.Bookings.Add(saved);
            // Historical-state fixtures include Refunded, whose domain workflow is still deferred.
            db.Entry(saved).Property(b => b.Status).CurrentValue = (BookingStatus)status;
            db.Entry(saved).Property(b => b.SessionId).CurrentValue = headerSessionId ?? session.Id;
        }
        else
        {
            db.SeatHolds.Add(hold);
            db.Entry(hold).Property(h => h.Status).CurrentValue = (SeatHoldStatus)status;
            db.Entry(hold).Property(h => h.SessionId).CurrentValue = headerSessionId ?? session.Id;
        }
        await db.SaveChangesAsync();
    }

    private async Task<string> CaptureEditState()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        return JsonSerializer.Serialize(new
        {
            Auditoriums = await db.Auditoriums.AsNoTracking().OrderBy(s => s.Id).ToArrayAsync(),
            Seats = await db.Seats.AsNoTracking().OrderBy(s => s.Id).ToArrayAsync(),
            Sessions = await db.Sessions.AsNoTracking().OrderBy(s => s.Id).ToArrayAsync(),
            Inventory = await db.SessionSeats.AsNoTracking().OrderBy(s => s.Id).ToArrayAsync(),
            Holds = await db.SeatHolds.AsNoTracking().Include(h => h.Items).OrderBy(h => h.Id).ToArrayAsync(),
            Bookings = await db.Bookings.AsNoTracking().Include(b => b.Items).OrderBy(b => b.Id).ToArrayAsync()
        });
    }
}
