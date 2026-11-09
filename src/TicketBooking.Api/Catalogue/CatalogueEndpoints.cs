using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketBooking.Api.Errors;
using TicketBooking.Api.Validation;
using TicketBooking.Application.Catalogue;
using TicketBooking.Contracts.Catalogue;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Infrastructure.Caching;
using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.Api.Catalogue;

public static class CatalogueEndpoints
{
    public static IEndpointRouteBuilder MapCatalogueEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/events", GetEventsAsync).AllowAnonymous().WithTags("Catalogue").ProducesProblem(500)
            .Produces<EventListResponse>().ProducesValidationProblem();
        endpoints.MapGet("/api/events/{id:guid}", GetEventAsync).AllowAnonymous().WithTags("Catalogue").ProducesProblem(500)
            .Produces<EventSummaryResponse>().ProducesProblem(404);
        endpoints.MapGet("/api/events/{id:guid}/sessions", GetEventSessionsAsync).AllowAnonymous().WithTags("Catalogue").ProducesProblem(500)
            .Produces<EventSessionListResponse>().ProducesValidationProblem().ProducesProblem(404);
        endpoints.MapGet("/api/sessions/{id:guid}", GetSessionAsync).AllowAnonymous().WithTags("Catalogue").ProducesProblem(500)
            .Produces<SessionDetailsResponse>().ProducesProblem(404);
        endpoints.MapGet("/api/sessions/{id:guid}/seats", GetSessionSeatsAsync).AllowAnonymous().WithTags("Catalogue").ProducesProblem(500)
            .Produces<SessionSeatMapResponse>().ProducesProblem(404).ProducesProblem(409);
        return endpoints;
    }

    private static async Task<Results<Ok<EventListResponse>, ProblemHttpResult>> GetEventsAsync(TicketBookingDbContext db, HttpContext context, IValidator<CatalogueQuery> validator)
    {
        var input = CatalogueQuery.Bind(context.Request.Query, includeText: true);
        var validation = await validator.ValidateAsync(input, context.RequestAborted);
        if (!validation.IsValid) return ApiProblemDetails.Validation(context, validation);
        var query = input.Query?.Trim();
        var (page, pageSize, offset, date, venueId) = input.Parse();

        var events = db.Events.AsNoTracking().Where(e => e.Status == EventStatus.Published);
        if (!string.IsNullOrEmpty(query))
        {
            // Treat wildcard characters as literal search text, not caller-controlled LIKE syntax.
            var pattern = "%" + query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            events = events.Where(e => EF.Functions.ILike(e.Title, pattern, "\\") || EF.Functions.ILike(e.Description, pattern, "\\"));
        }
        if (date is not null || venueId is not null)
        {
            var sessions = db.Sessions.Where(s => s.Status == SessionStatus.Scheduled);
            if (date is { } day)
            {
                var from = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
                sessions = sessions.Where(s => s.StartsAt >= from);
                if (day != DateOnly.MaxValue)
                {
                    var until = from.AddDays(1);
                    sessions = sessions.Where(s => s.StartsAt < until);
                }
            }
            if (venueId is { } venue)
                sessions = sessions.Where(s => db.Auditoriums.Any(a => a.Id == s.AuditoriumId && a.VenueId == venue));
            events = events.Where(e => sessions.Any(s => s.EventId == e.Id));
        }

        var ct = context.RequestAborted;
        // Keep count and page contents consistent while Admins publish or reschedule events.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var totalCount = await events.CountAsync(ct);
        var items = await events.OrderBy(e => e.Title).ThenBy(e => e.Id).Skip((int)offset).Take(pageSize)
            .Select(e => new EventSummaryResponse(e.Id, e.Title, e.Description, (int)e.Duration.TotalMinutes, e.AgeRating)).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.Ok(new EventListResponse(items, page, pageSize, totalCount));
    }

    private static async Task<Results<Ok<EventSummaryResponse>, NotFound>> GetEventAsync(Guid id, TicketBookingDbContext db, CancellationToken ct)
    {
        var offering = await db.Events.AsNoTracking().Where(e => e.Id == id && e.Status == EventStatus.Published)
            .Select(e => new EventSummaryResponse(e.Id, e.Title, e.Description, (int)e.Duration.TotalMinutes, e.AgeRating))
            .SingleOrDefaultAsync(ct);
        return offering is null ? TypedResults.NotFound() : TypedResults.Ok(offering);
    }

    private static async Task<Results<Ok<EventSessionListResponse>, NotFound, ProblemHttpResult>> GetEventSessionsAsync(Guid id, TicketBookingDbContext db, HttpContext context, IValidator<CatalogueQuery> validator)
    {
        var input = CatalogueQuery.Bind(context.Request.Query, includeText: false);
        var validation = await validator.ValidateAsync(input, context.RequestAborted);
        if (!validation.IsValid) return ApiProblemDetails.Validation(context, validation);
        var (page, pageSize, offset, date, venueId) = input.Parse();
        var ct = context.RequestAborted;
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        if (!await db.Events.AnyAsync(e => e.Id == id && e.Status == EventStatus.Published, ct)) return TypedResults.NotFound();
        var sessions = PublicSessions(db).Where(s => s.EventId == id);
        if (date is { } day)
        {
            var from = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            sessions = sessions.Where(s => s.StartsAt >= from);
            if (day != DateOnly.MaxValue)
            {
                var until = from.AddDays(1);
                sessions = sessions.Where(s => s.StartsAt < until);
            }
        }
        if (venueId is { } venue)
            sessions = sessions.Where(s => db.Auditoriums.Any(a => a.Id == s.AuditoriumId && a.VenueId == venue));
        var totalCount = await sessions.CountAsync(ct);
        var items = await ProjectSessions(sessions, db).Skip((int)offset).Take(pageSize).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.Ok(new EventSessionListResponse(items, page, pageSize, totalCount));
    }

    private static async Task<Results<Ok<SessionDetailsResponse>, NotFound>> GetSessionAsync(Guid id, TicketBookingDbContext db, CancellationToken ct)
    {
        var session = await ProjectSessions(PublicSessions(db).Where(s => s.Id == id), db).SingleOrDefaultAsync(ct);
        return session is null ? TypedResults.NotFound() : TypedResults.Ok(session);
    }

    private static async Task<Results<Ok<SessionSeatMapResponse>, NotFound, ProblemHttpResult>> GetSessionSeatsAsync(Guid id, TicketBookingDbContext db,
        ISeatAvailabilityCache cache, HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var ct = context.RequestAborted;
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var session = await PublicSessions(db).SingleOrDefaultAsync(s => s.Id == id, ct);
        if (session is null) return TypedResults.NotFound();
        var seatCount = await db.Seats.CountAsync(s => s.AuditoriumId == session.AuditoriumId, ct);
        SeatAvailabilityCacheKey? cacheKey = null;
        if (cache.IsUseful(seatCount))
        {
            // Cheap inventory metadata comes from the same database snapshot as visibility/layout checks.
            // Every supported allocation write advances Version; a commit selects a new cache key.
            var versions = await db.SessionSeats.AsNoTracking().Where(seat => seat.SessionId == id)
                .Select(seat => new SeatAvailabilityVersion(seat.Id, seat.Version)).ToArrayAsync(ct);
            if (versions.Length == seatCount)
            {
                cacheKey = new SeatAvailabilityCacheKey(id, session.AuditoriumId, SeatAvailabilityRevision.Create(versions));
                var cached = await cache.GetAsync(cacheKey, ct);
                if (cached is not null)
                {
                    await transaction.CommitAsync(ct);
                    return TypedResults.Ok(cached);
                }
            }
        }
        var inventory = await (from item in db.SessionSeats.AsNoTracking()
                               join seat in db.Seats on item.SeatId equals seat.Id
                               where item.SessionId == id
                               orderby seat.Row, seat.Number, seat.Id
                               select new
                               {
                                   seat.AuditoriumId,
                                   Seat = new SeatMapSeatResponse(item.Id, seat.Id, seat.Row, seat.Number, (int)seat.Type,
                                       item.Price.Amount, item.Price.Currency, (int)item.Status, item.Version)
                               }).ToListAsync(ct);
        if (inventory.Count != seatCount || inventory.Any(s => s.AuditoriumId != session.AuditoriumId))
        {
            var problem = new ProblemDetails { Status = 409, Title = "Conflict", Detail = "Session inventory does not match its auditorium layout." };
            ApiProblemDetails.Enrich(context, problem);
            problem.Type = $"urn:ticketbooking:error:{problem.Extensions["code"]}";
            return TypedResults.Problem(problem);
        }
        await transaction.CommitAsync(ct);
        var response = new SessionSeatMapResponse(id, session.AuditoriumId, inventory.Select(s => s.Seat).ToArray());
        if (cacheKey is not null) await cache.SetAsync(cacheKey, response, ct);
        return TypedResults.Ok(response);
    }

    private static IQueryable<Session> PublicSessions(TicketBookingDbContext db) =>
        db.Sessions.AsNoTracking().Where(s => s.Status == SessionStatus.Scheduled &&
            db.Events.Any(e => e.Id == s.EventId && e.Status == EventStatus.Published));

    private static IQueryable<SessionDetailsResponse> ProjectSessions(IQueryable<Session> sessions, TicketBookingDbContext db) =>
        from session in sessions
        join offering in db.Events on session.EventId equals offering.Id
        join auditorium in db.Auditoriums on session.AuditoriumId equals auditorium.Id
        join venue in db.Venues on auditorium.VenueId equals venue.Id
        orderby session.StartsAt, session.Id
        select new SessionDetailsResponse(session.Id, offering.Id, offering.Title, auditorium.Id, auditorium.Name,
            venue.Id, venue.Name, venue.Address, session.StartsAt, session.EndsAt, (int)session.Status);

}
