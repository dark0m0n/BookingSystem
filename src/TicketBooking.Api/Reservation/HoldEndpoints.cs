using Microsoft.EntityFrameworkCore;
using TicketBooking.Api.Auth;
using TicketBooking.Api.Validation;
using TicketBooking.Application.Identity;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Domain;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.Api.Reservation;

public static class HoldEndpoints
{
    public static IEndpointRouteBuilder MapHoldEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/sessions/{sessionId:guid}/holds", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.Customer).WithTags("Reservation")
            .ValidateRequest<CreateHoldRequest>().Produces<HoldResponse>(201)
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(500);
        var holds = endpoints.MapGroup("/api/holds").RequireAuthorization(AuthorizationPolicies.Customer)
            .WithTags("Reservation").ProducesProblem(401).ProducesProblem(403).ProducesProblem(500);
        holds.MapGet("/{holdId:guid}", GetAsync).Produces<HoldResponse>().ProducesProblem(404);
        holds.MapDelete("/{holdId:guid}", CancelAsync).Produces(204).ProducesProblem(404).ProducesProblem(409);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(Guid holdId, TicketBookingDbContext db,
        ResourceOwnershipService ownership, HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var hold = await db.SeatHolds.AsNoTracking().Include(value => value.Items).AsSingleQuery()
            .SingleOrDefaultAsync(value => value.Id == holdId, context.RequestAborted);
        if (hold is null) return Results.NotFound();
        ownership.EnsureHoldOwner(hold);
        return Results.Ok(ToResponse(hold));
    }

    private static async Task<IResult> CancelAsync(Guid holdId, TicketBookingDbContext db,
        SessionSeatAllocationWriter writer, ICurrentUser currentUser, ResourceOwnershipService ownership,
        TimeProvider clock, HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var ct = context.RequestAborted;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize hold transitions before inspecting stored ownership/state. Lock scalar IDs because
        // EF materializes the hold's Money complex value through the mapped query below.
        var locked = await db.Database.SqlQuery<Guid>(
            $"SELECT \"Id\" AS \"Value\" FROM \"SeatHolds\" WHERE \"Id\" = {holdId} FOR UPDATE").ToListAsync(ct);
        if (locked.Count == 0) return Results.NotFound();
        var hold = await db.SeatHolds.Include(value => value.Items).SingleAsync(value => value.Id == holdId, ct);
        ownership.EnsureHoldOwner(hold);
        var userId = currentUser.UserId ?? throw new AuthenticationRequiredException();
        // Authorize before replaying success. A completed cancellation must never touch inventory again.
        if (hold.Status == SeatHoldStatus.Cancelled) return Results.NoContent();
        var session = await db.Sessions.FromSqlInterpolated(
            $"SELECT * FROM \"Sessions\" WHERE \"Id\" = {hold.SessionId} FOR SHARE").AsNoTracking().SingleAsync(ct);
        hold.EnsureCanConvert(userId, clock);
        var items = hold.Items.OrderBy(item => item.SessionSeatId).ToArray();
        if (items.Length == 0 || items.Any(item => item.AllocationVersion is null or <= 0 or long.MaxValue))
            throw new SeatAllocationConflictException();
        var ids = items.Select(item => item.SessionSeatId).ToArray();
        if (await db.SessionSeats.CountAsync(seat => ids.Contains(seat.Id) && seat.SessionId == hold.SessionId, ct) != items.Length)
            throw new SeatAllocationConflictException();
        var releasedStatus = session.Status == SessionStatus.Scheduled && session.StartsAt > clock.GetUtcNow()
            ? SessionSeatStatus.Available : SessionSeatStatus.Unavailable;
        foreach (var item in items)
            await writer.ReleaseAsync(item.SessionSeatId, item.AllocationVersion!.Value, SessionSeatStatus.Held, releasedStatus, ct);
        // Sample the clock again after any wait for inventory locks. Late cancellation rolls back releases.
        hold.Cancel(userId, clock);
        await db.SaveChangesAsync(ct);
        if (clock.GetUtcNow() >= hold.ExpiresAt)
            throw new DomainRuleException(DomainError.HoldExpired, "The hold expired during cancellation.");
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateAsync(Guid sessionId, CreateHoldRequest request,
        TicketBookingDbContext db, SessionSeatAllocationWriter writer, ICurrentUser currentUser,
        ResourceOwnershipService ownership, TimeProvider clock, BookingTimingPolicy timing, HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var userId = currentUser.UserId is { } id && id != Guid.Empty ? id : throw new AuthenticationRequiredException();
        var ct = context.RequestAborted;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Compatible shared locks allow independent claims while excluding administrative schedule/event edits.
        var session = await db.Sessions.FromSqlInterpolated($"SELECT * FROM \"Sessions\" WHERE \"Id\" = {sessionId} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (session is null) return Results.NotFound();
        var offering = await db.Events.FromSqlInterpolated($"SELECT * FROM \"Events\" WHERE \"Id\" = {session.EventId} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (offering is null || offering.Status != EventStatus.Published) return Results.NotFound();
        if (session.Status != SessionStatus.Scheduled || session.StartsAt <= clock.GetUtcNow())
            return Results.Conflict();

        var ids = request.SessionSeatIds!.ToArray();
        var seats = await db.SessionSeats.AsNoTracking().Where(seat => seat.SessionId == sessionId && ids.Contains(seat.Id))
            .OrderBy(seat => seat.Id).ToListAsync(ct);
        if (seats.Count != ids.Length) return Results.NotFound();
        var physicalIds = seats.Select(value => value.SeatId).ToArray();
        if (await db.Seats.CountAsync(seat => seat.AuditoriumId == session.AuditoriumId && physicalIds.Contains(seat.Id), ct) != seats.Count)
            return Results.Conflict();
        if (seats.Any(seat => seat.Status != SessionSeatStatus.Available || seat.Version == long.MaxValue))
            throw new DomainRuleException(DomainError.SeatNotAvailable, "The selected seats cannot be claimed.");
        if (seats.Select(seat => seat.Price.Currency).Distinct().Count() != 1 ||
            seats.Sum(seat => seat.Price.Amount) > Money.MaxAmount)
            return Results.Conflict();

        // PostgreSQL stores microsecond precision; persist and return the same captured deadline.
        var instant = clock.GetUtcNow().ToUniversalTime();
        var captured = new DateTimeOffset(instant.Ticks - instant.Ticks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
        if (session.StartsAt <= instant) return Results.Conflict();
        var hold = new SeatHold(Guid.NewGuid(), sessionId, userId, session.StartsAt, seats, new CapturedClock(captured), timing);
        ownership.EnsureHoldOwner(hold);
        var items = hold.Items.ToDictionary(item => item.SessionSeatId);
        // Stable order avoids deadlocks for overlapping multi-seat selections. Never retry a stale version.
        foreach (var seat in seats)
            items[seat.Id].CaptureAllocationVersion(await writer.HoldAsync(seat.Id, seat.Version, ct));
        db.SeatHolds.Add(hold);
        await db.SaveChangesAsync(ct);
        if (clock.GetUtcNow() >= hold.ExpiresAt)
            throw new DomainRuleException(DomainError.HoldExpired, "The hold expired during allocation.");
        await transaction.CommitAsync(ct);
        return Results.Created($"/api/holds/{hold.Id}", ToResponse(hold));
    }

    private static HoldResponse ToResponse(SeatHold hold) => new(hold.Id, hold.SessionId, hold.ExpiresAt, (int)hold.Status,
            hold.TotalAmount.Amount, hold.TotalAmount.Currency,
            hold.Items.OrderBy(item => item.SessionSeatId)
                .Select(item => new HoldItemResponse(item.SessionSeatId, item.PriceSnapshot.Amount, item.PriceSnapshot.Currency)).ToArray());

    private sealed class CapturedClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
