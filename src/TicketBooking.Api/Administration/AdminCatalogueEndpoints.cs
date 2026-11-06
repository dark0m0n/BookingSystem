using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketBooking.Api.Auth;
using TicketBooking.Api.Errors;
using TicketBooking.Api.Validation;
using TicketBooking.Contracts.Administration;
using TicketBooking.Domain;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Venue;
using TicketBooking.Infrastructure.Persistence;
using VenueEntity = TicketBooking.Domain.Venue.Venue;

namespace TicketBooking.Api.Administration;

public static class AdminCatalogueEndpoints
{
    public static IEndpointRouteBuilder MapAdminCatalogueEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin").RequireAuthorization(AuthorizationPolicies.Admin).WithTags("Administration")
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(500);
        group.MapPost("/venues", CreateVenueAsync).ValidateRequest<CreateVenueRequest>().Produces<VenueResponse>(201).ProducesValidationProblem();
        group.MapGet("/venues", async (TicketBookingDbContext db, CancellationToken ct) =>
            await db.Venues.AsNoTracking().OrderBy(v => v.Name).ThenBy(v => v.Id)
                .Select(v => new VenueResponse(v.Id, v.Name, v.Address)).ToListAsync(ct));
        group.MapGet("/venues/{id:guid}", GetVenueAsync).Produces<VenueResponse>().ProducesProblem(404);
        group.MapPost("/auditoriums", CreateAuditoriumAsync).ValidateRequest<CreateAuditoriumRequest>().Produces<AuditoriumResponse>(201).ProducesValidationProblem().ProducesProblem(404);
        group.MapGet("/venues/{venueId:guid}/auditoriums", GetAuditoriumsAsync).Produces<AuditoriumResponse[]>().ProducesProblem(404);
        group.MapGet("/auditoriums/{id:guid}", GetAuditoriumAsync).Produces<AuditoriumResponse>().ProducesProblem(404);
        group.MapPatch("/auditoriums/{id:guid}", RenameAuditoriumAsync).ValidateRequest<UpdateAuditoriumRequest>().Produces<AuditoriumResponse>().ProducesProblem(404);
        group.MapDelete("/auditoriums/{id:guid}", DeleteAuditoriumAsync).Produces(204).ProducesProblem(404).ProducesProblem(409);
        group.MapPost("/auditoriums/{id:guid}/seats", AddSeatsAsync).ValidateRequest<AddSeatsRequest>().Produces<SeatResponse[]>(201).ProducesProblem(404).ProducesProblem(409);
        group.MapGet("/auditoriums/{id:guid}/seats", GetSeatsAsync).Produces<SeatResponse[]>().ProducesProblem(404);
        group.MapPatch("/auditoriums/{id:guid}/seats/{seatId:guid}", UpdateSeatAsync).ValidateRequest<SeatRequest>()
            .Produces<SeatResponse>().ProducesValidationProblem().ProducesProblem(404).ProducesProblem(409);
        group.MapDelete("/auditoriums/{id:guid}/seats/{seatId:guid}", DeleteSeatAsync).Produces(204).ProducesProblem(404).ProducesProblem(409);
        group.MapPost("/events", CreateEventAsync).ValidateRequest<CreateEventRequest>().Produces<EventResponse>(201).ProducesValidationProblem();
        group.MapGet("/events/{id:guid}", GetEventAsync).Produces<EventResponse>().ProducesProblem(404);
        group.MapPatch("/events/{id:guid}", UpdateEventAsync).ValidateRequest<UpdateEventRequest>()
            .Produces<EventResponse>().ProducesValidationProblem().ProducesProblem(404);
        group.MapPost("/sessions", CreateSessionAsync).ValidateRequest<CreateSessionRequest>().Produces<SessionResponse>(201).ProducesValidationProblem().ProducesProblem(404).ProducesProblem(409);
        group.MapGet("/sessions/{id:guid}", GetSessionAsync).Produces<SessionResponse>().ProducesProblem(404);
        group.MapPatch("/sessions/{id:guid}", UpdateSessionAsync).ValidateRequest<UpdateSessionRequest>()
            .Produces<SessionResponse>().ProducesValidationProblem().ProducesProblem(404).ProducesProblem(409);
        return endpoints;
    }

    private static async Task<IResult> CreateVenueAsync(CreateVenueRequest request, TicketBookingDbContext db, HttpContext context)
    {
        var venue = new VenueEntity(Guid.NewGuid(), request.Name!.Trim(), request.Address!.Trim());
        db.Venues.Add(venue);
        await db.SaveChangesAsync(context.RequestAborted);
        return Results.Created($"/api/admin/venues/{venue.Id}", new VenueResponse(venue.Id, venue.Name, venue.Address));
    }

    private static async Task<IResult> GetVenueAsync(Guid id, TicketBookingDbContext db, CancellationToken ct)
    {
        var venue = await db.Venues.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, ct);
        return venue is null ? Results.NotFound() : Results.Ok(new VenueResponse(venue.Id, venue.Name, venue.Address));
    }

    private static async Task<IResult> CreateAuditoriumAsync(CreateAuditoriumRequest request, TicketBookingDbContext db, HttpContext context)
    {
        if (!await db.Venues.AnyAsync(v => v.Id == request.VenueId, context.RequestAborted)) return Results.NotFound();
        var auditorium = new Auditorium(Guid.NewGuid(), request.VenueId, request.Name!.Trim());
        db.Auditoriums.Add(auditorium);
        await db.SaveChangesAsync(context.RequestAborted);
        return Results.Created($"/api/admin/auditoriums/{auditorium.Id}", AuditoriumDto(auditorium));
    }

    private static async Task<IResult> GetAuditoriumsAsync(Guid venueId, TicketBookingDbContext db, CancellationToken ct)
    {
        if (!await db.Venues.AnyAsync(v => v.Id == venueId, ct)) return Results.NotFound();
        return Results.Ok(await db.Auditoriums.AsNoTracking().Where(a => a.VenueId == venueId)
            .OrderBy(a => a.Name).ThenBy(a => a.Id).Select(a => new AuditoriumResponse(a.Id, a.VenueId, a.Name)).ToListAsync(ct));
    }

    private static async Task<IResult> GetAuditoriumAsync(Guid id, TicketBookingDbContext db, CancellationToken ct)
    {
        var auditorium = await db.Auditoriums.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);
        return auditorium is null ? Results.NotFound() : Results.Ok(AuditoriumDto(auditorium));
    }

    private static async Task<IResult> RenameAuditoriumAsync(Guid id, UpdateAuditoriumRequest request, TicketBookingDbContext db, HttpContext context)
    {
        var auditorium = await db.Auditoriums.SingleOrDefaultAsync(a => a.Id == id, context.RequestAborted);
        if (auditorium is null) return Results.NotFound();
        auditorium.Rename(request.Name!.Trim());
        await db.SaveChangesAsync(context.RequestAborted);
        return Results.Ok(AuditoriumDto(auditorium));
    }

    private static async Task<IResult> DeleteAuditoriumAsync(Guid id, TicketBookingDbContext db, HttpContext context)
    {
        var ct = context.RequestAborted;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var auditorium = await LockAuditorium(db, id, ct);
        if (auditorium is null) return Results.NotFound();
        if (await HasLayoutReferences(db, id, ct)) return Conflict(context, "An auditorium used by a session cannot be deleted.");
        db.Seats.RemoveRange(await db.Seats.Where(s => s.AuditoriumId == id).ToListAsync(ct));
        db.Auditoriums.Remove(auditorium);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> AddSeatsAsync(Guid id, AddSeatsRequest request, TicketBookingDbContext db, HttpContext context)
    {
        var seats = request.Seats!.Select(s => new Seat(Guid.NewGuid(), id, s!.Row!.Trim(), s.Number, (SeatType)s.Type)).ToArray();
        if (seats.Select(s => (s.Row, s.Number)).Distinct().Count() != seats.Length)
            return Conflict(context, "Seat coordinates must be unique within the auditorium.");
        var ct = context.RequestAborted;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await LockAuditorium(db, id, ct) is null) return Results.NotFound();
        if (await HasLayoutReferences(db, id, ct)) return Conflict(context, "The layout of an auditorium used by a session is fixed.");
        var existing = await db.Seats.Where(s => s.AuditoriumId == id).Select(s => new { s.Row, s.Number }).ToListAsync(ct);
        var coordinates = existing.Select(s => (s.Row, s.Number)).ToHashSet();
        if (seats.Any(s => coordinates.Contains((s.Row, s.Number)))) return Conflict(context, "A seat coordinate already exists.");
        db.Seats.AddRange(seats);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Created($"/api/admin/auditoriums/{id}/seats", seats.Select(SeatDto).ToArray());
    }

    private static async Task<IResult> GetSeatsAsync(Guid id, TicketBookingDbContext db, CancellationToken ct)
    {
        if (!await db.Auditoriums.AnyAsync(a => a.Id == id, ct)) return Results.NotFound();
        return Results.Ok(await db.Seats.AsNoTracking().Where(s => s.AuditoriumId == id).OrderBy(s => s.Row).ThenBy(s => s.Number)
            .Select(s => new SeatResponse(s.Id, s.AuditoriumId, s.Row, s.Number, (int)s.Type)).ToListAsync(ct));
    }

    private static async Task<IResult> DeleteSeatAsync(Guid id, Guid seatId, TicketBookingDbContext db, HttpContext context)
    {
        var ct = context.RequestAborted;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await LockAuditorium(db, id, ct) is null) return Results.NotFound();
        var seat = await db.Seats.SingleOrDefaultAsync(s => s.Id == seatId && s.AuditoriumId == id, ct);
        if (seat is null) return Results.NotFound();
        if (await HasLayoutReferences(db, id, ct)) return Conflict(context, "The layout of an auditorium used by a session is fixed.");
        db.Seats.Remove(seat);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateSeatAsync(Guid id, Guid seatId, SeatRequest request, TicketBookingDbContext db, HttpContext context)
    {
        var ct = context.RequestAborted;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await LockAuditorium(db, id, ct) is null) return Results.NotFound();
        var seat = await db.Seats.SingleOrDefaultAsync(s => s.Id == seatId && s.AuditoriumId == id, ct);
        if (seat is null) return Results.NotFound();
        if (await HasLayoutReferences(db, id, ct)) return Conflict(context, "The layout of an auditorium used by a session is fixed.");
        var row = request.Row!.Trim();
        if (await db.Seats.AnyAsync(s => s.AuditoriumId == id && s.Id != seatId && s.Row == row && s.Number == request.Number, ct))
            return Conflict(context, "A seat coordinate already exists.");

        seat.UpdateLayout(row, request.Number, (SeatType)request.Type);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(SeatDto(seat));
    }

    private static async Task<IResult> CreateEventAsync(CreateEventRequest request, TicketBookingDbContext db, HttpContext context)
    {
        var offering = new Event(Guid.NewGuid(), request.Title!.Trim(), request.Description!, TimeSpan.FromMinutes(request.DurationMinutes),
            request.AgeRating, (EventStatus)request.Status);
        db.Events.Add(offering);
        await db.SaveChangesAsync(context.RequestAborted);
        return Results.Created($"/api/admin/events/{offering.Id}", EventDto(offering));
    }

    private static async Task<IResult> GetEventAsync(Guid id, TicketBookingDbContext db, CancellationToken ct)
    {
        var offering = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, ct);
        return offering is null ? Results.NotFound() : Results.Ok(EventDto(offering));
    }

    private static async Task<IResult> UpdateEventAsync(Guid id, UpdateEventRequest request, TicketBookingDbContext db, HttpContext context)
    {
        var ct = context.RequestAborted;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var offering = await db.Events.FromSqlInterpolated($"SELECT * FROM \"Events\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (offering is null) return Results.NotFound();
        offering.UpdateDetails(request.Title?.Trim() ?? offering.Title, request.Description ?? offering.Description,
            request.DurationMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : offering.Duration,
            request.AgeRating ?? offering.AgeRating, request.Status is { } status ? (EventStatus)status : offering.Status);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(EventDto(offering));
    }

    private static async Task<IResult> CreateSessionAsync(CreateSessionRequest request, TicketBookingDbContext db, TimeProvider clock, HttpContext context)
    {
        var price = new Money(request.Price!.Value, request.Currency!);
        var ct = context.RequestAborted;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await LockAuditorium(db, request.AuditoriumId, ct) is null) return Results.NotFound();
        // Hold a shared event lock until commit so publication/duration cannot change mid-scheduling.
        var offering = await db.Events.FromSqlInterpolated($"SELECT * FROM \"Events\" WHERE \"Id\" = {request.EventId} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (offering is null) return Results.NotFound();
        if (offering.Status != EventStatus.Published) return Conflict(context, "Only a published event can be scheduled.");
        var seats = await db.Seats.AsNoTracking().Where(s => s.AuditoriumId == request.AuditoriumId)
            .OrderBy(s => s.Row).ThenBy(s => s.Number).ToListAsync(ct);
        if (seats.Count == 0) return Conflict(context, "An auditorium must have seats before a session is created.");
        var startsAt = NormalizeInstant(request.StartsAt!.Value);
        DateTimeOffset endsAt;
        try { endsAt = startsAt.Add(offering.Duration); }
        catch (ArgumentOutOfRangeException) { return Invalid(context, new Dictionary<string, string[]> { ["startsAt"] = ["Session end exceeds the supported date range."] }); }
        // The lock may have waited; recheck eligibility immediately before inserting the session.
        if (startsAt <= clock.GetUtcNow()) return Invalid(context, new Dictionary<string, string[]> { ["startsAt"] = ["Session start must be in the future."] });
        var session = new Session(Guid.NewGuid(), offering.Id, request.AuditoriumId, startsAt, endsAt);
        var inventory = seats.Select(s => new SessionSeat(Guid.NewGuid(), session.Id, s.Id, price!)).ToArray();
        db.Sessions.Add(session);
        db.SessionSeats.AddRange(inventory);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Created($"/api/admin/sessions/{session.Id}", SessionDto(session, inventory, seats));
    }

    private static async Task<IResult> GetSessionAsync(Guid id, TicketBookingDbContext db, CancellationToken ct)
    {
        // Keep the session, inventory and physical layout in one snapshot during auditorium moves.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct);
        if (session is null) return Results.NotFound();
        var inventory = await db.SessionSeats.AsNoTracking().Where(s => s.SessionId == id).ToListAsync(ct);
        var seats = await db.Seats.AsNoTracking().Where(s => s.AuditoriumId == session.AuditoriumId)
            .OrderBy(s => s.Row).ThenBy(s => s.Number).ToListAsync(ct);
        var response = SessionDto(session, inventory, seats);
        await transaction.CommitAsync(ct);
        return Results.Ok(response);
    }

    private static async Task<IResult> UpdateSessionAsync(Guid id, UpdateSessionRequest request, TicketBookingDbContext db,
        TimeProvider clock, HttpContext context)
    {
        var ct = context.RequestAborted;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Do not block FK key-share locks acquired by allocation transactions before inventory locking.
        var session = await db.Sessions.FromSqlInterpolated($"SELECT * FROM \"Sessions\" WHERE \"Id\" = {id} FOR NO KEY UPDATE")
            .SingleOrDefaultAsync(ct);
        if (session is null) return Results.NotFound();
        if (session.Status != SessionStatus.Scheduled || session.StartsAt <= clock.GetUtcNow())
            return Conflict(context, "Only future scheduled sessions can be updated.");

        var auditoriumId = request.AuditoriumId ?? session.AuditoriumId;
        // Stable ordering also serializes opposite-direction moves without deadlocks.
        foreach (var hallId in new[] { session.AuditoriumId, auditoriumId }.Distinct().Order())
            if (await LockAuditorium(db, hallId, ct) is null) return Results.NotFound();
        var eventId = request.EventId ?? session.EventId;
        var offering = await db.Events.FromSqlInterpolated($"SELECT * FROM \"Events\" WHERE \"Id\" = {eventId} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (offering is null) return Results.NotFound();
        if (offering.Status != EventStatus.Published) return Conflict(context, "Only a published event can be scheduled.");
        // Lock scalar IDs first, then let the mapped query materialize the Money complex value.
        await db.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM \"SessionSeats\" WHERE \"SessionId\" = {id} ORDER BY \"Id\" FOR UPDATE")
            .ToListAsync(ct);
        var inventory = await db.SessionSeats.Where(s => s.SessionId == id).OrderBy(s => s.Id).ToListAsync(ct);
        if (inventory.Count == 0 || inventory.Any(s => s.Status != SessionSeatStatus.Available) ||
            await HasReservationReferences(db, id, inventory.Select(s => s.Id).ToArray(), ct))
            return Conflict(context, "Sessions with allocated inventory or hold/booking history cannot be updated.");
        var sourceSeatIds = await db.Seats.Where(s => s.AuditoriumId == session.AuditoriumId).Select(s => s.Id).ToListAsync(ct);
        if (!sourceSeatIds.ToHashSet().SetEquals(inventory.Select(s => s.SeatId)))
            return Conflict(context, "Session inventory must match its auditorium layout.");
        var seats = await db.Seats.AsNoTracking().Where(s => s.AuditoriumId == auditoriumId)
            .OrderBy(s => s.Row).ThenBy(s => s.Number).ToListAsync(ct);
        if (seats.Count == 0) return Conflict(context, "An auditorium must have seats before a session is scheduled.");

        var startsAt = NormalizeInstant(request.StartsAt ?? session.StartsAt);
        // Keep the captured duration unless the event is replaced.
        var duration = eventId == session.EventId ? session.EndsAt - session.StartsAt : offering.Duration;
        DateTimeOffset endsAt;
        try { endsAt = startsAt.Add(duration); }
        catch (ArgumentOutOfRangeException) { return Invalid(context, new Dictionary<string, string[]> { ["startsAt"] = ["Session end exceeds the supported date range."] }); }
        if (session.StartsAt <= clock.GetUtcNow()) return Conflict(context, "Only future scheduled sessions can be updated.");
        if (startsAt <= clock.GetUtcNow()) return Invalid(context, new Dictionary<string, string[]> { ["startsAt"] = ["Session start must be in the future."] });

        if (auditoriumId != session.AuditoriumId)
        {
            if (inventory.Select(s => s.Price).Distinct().Count() != 1)
                return Conflict(context, "Moving a session requires a uniform stored seat price.");
            var price = inventory[0].Price;
            // Tracked deletes retain expected-version predicates; do not replace these with bulk deletes.
            db.SessionSeats.RemoveRange(inventory);
            inventory = seats.Select(s => new SessionSeat(Guid.NewGuid(), id, s.Id, price)).ToList();
            db.SessionSeats.AddRange(inventory);
        }
        else
        {
            // Fence clients that loaded inventory before a schedule change, even when seats remain Available.
            foreach (var seat in inventory) db.Entry(seat).Property(s => s.Version).IsModified = true;
        }
        session.UpdateSchedule(eventId, auditoriumId, startsAt, endsAt);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(SessionDto(session, inventory, seats));
    }

    private static DateTimeOffset NormalizeInstant(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
    }

    private static Task<Auditorium?> LockAuditorium(TicketBookingDbContext db, Guid id, CancellationToken ct) =>
        db.Auditoriums.FromSqlInterpolated($"SELECT * FROM \"Auditoriums\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
    private static async Task<bool> HasLayoutReferences(TicketBookingDbContext db, Guid id, CancellationToken ct) =>
        await db.Sessions.AnyAsync(s => s.AuditoriumId == id, ct) ||
        // Protect physical seats even if an inventory row has an inconsistent session/auditorium relationship.
        await db.SessionSeats.AnyAsync(inventory => db.Seats.Any(seat => seat.Id == inventory.SeatId && seat.AuditoriumId == id), ct);

    private static async Task<bool> HasReservationReferences(TicketBookingDbContext db, Guid sessionId, Guid[] inventoryIds, CancellationToken ct) =>
        await db.SeatHolds.AnyAsync(hold => hold.SessionId == sessionId, ct) ||
        await db.Bookings.AnyAsync(booking => booking.SessionId == sessionId, ct) ||
        // Item links are authoritative references too; header-only checks can miss inconsistent historical data.
        await db.SeatHoldItems.AnyAsync(item => inventoryIds.Contains(item.SessionSeatId), ct) ||
        await db.BookingItems.AnyAsync(item => inventoryIds.Contains(item.SessionSeatId), ct);
    private static AuditoriumResponse AuditoriumDto(Auditorium a) => new(a.Id, a.VenueId, a.Name);
    private static SeatResponse SeatDto(Seat s) => new(s.Id, s.AuditoriumId, s.Row, s.Number, (int)s.Type);
    private static EventResponse EventDto(Event e) => new(e.Id, e.Title, e.Description, (int)e.Duration.TotalMinutes, e.AgeRating, (int)e.Status);
    private static SessionResponse SessionDto(Session session, IReadOnlyList<SessionSeat> inventory, IReadOnlyList<Seat> seats)
    {
        var physical = seats.ToDictionary(s => s.Id);
        return new(session.Id, session.EventId, session.AuditoriumId, session.StartsAt, session.EndsAt, (int)session.Status,
            inventory.OrderBy(s => physical[s.SeatId].Row, StringComparer.Ordinal).ThenBy(s => physical[s.SeatId].Number)
                .Select(s => new SessionSeatResponse(s.Id, s.SeatId, physical[s.SeatId].Row, physical[s.SeatId].Number,
                    (int)physical[s.SeatId].Type, s.Price.Amount, s.Price.Currency, (int)s.Status, s.Version)).ToArray());
    }
    private static IResult Invalid(HttpContext context, IDictionary<string, string[]> errors)
    {
        var problem = new HttpValidationProblemDetails(errors) { Status = 400, Title = "Invalid request" };
        ApiProblemDetails.Enrich(context, problem);
        problem.Type = $"urn:ticketbooking:error:{problem.Extensions["code"]}";
        return Results.Problem(problem);
    }
    private static IResult Conflict(HttpContext context, string detail)
    {
        var problem = new ProblemDetails { Status = 409, Title = "Conflict", Detail = detail };
        ApiProblemDetails.Enrich(context, problem);
        problem.Type = $"urn:ticketbooking:error:{problem.Extensions["code"]}";
        return Results.Problem(problem);
    }
}
