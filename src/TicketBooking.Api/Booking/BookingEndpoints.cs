using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TicketBooking.Api.Auth;
using TicketBooking.Api.Errors;
using TicketBooking.Api.Validation;
using TicketBooking.Application.Identity;
using TicketBooking.Contracts.Booking;
using TicketBooking.Infrastructure.Persistence;
using BookingEntity = TicketBooking.Domain.Booking.Booking;
using BookingItem = TicketBooking.Domain.Booking.BookingItem;

namespace TicketBooking.Api.Booking;

public static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/bookings", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.Customer).WithTags("Booking")
            .ValidateRequest<CreateBookingRequest>().Produces<BookingResponse>(201)
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(500);
        endpoints.MapGet("/api/bookings/{bookingId:guid}", GetAsync)
            .RequireAuthorization(AuthorizationPolicies.Customer).WithTags("Booking").Produces<BookingResponse>()
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(500);
        endpoints.MapGet("/api/me/bookings", GetMineAsync)
            .RequireAuthorization(AuthorizationPolicies.Customer).WithTags("Booking").Produces<BookingListResponse>()
            .ProducesValidationProblem().ProducesProblem(401).ProducesProblem(403).ProducesProblem(500);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(Guid bookingId, TicketBookingDbContext db,
        ResourceOwnershipService ownership, HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var ct = context.RequestAborted;
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(value => value.Id == bookingId, ct);
        if (booking is null) return Results.NotFound();
        ownership.EnsureBookingOwner(booking);
        var items = await db.BookingItems.AsNoTracking().Where(item => item.BookingId == bookingId)
            .OrderBy(item => item.SessionSeatId).ToArrayAsync(ct);
        var response = ToResponse(booking, items);
        await transaction.CommitAsync(ct);
        return Results.Ok(response);
    }

    private static async Task<IResult> GetMineAsync(TicketBookingDbContext db, ICurrentUser currentUser,
        ResourceOwnershipService ownership, IValidator<BookingListQuery> validator, HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var userId = currentUser.UserId is { } id && id != Guid.Empty ? id : throw new AuthenticationRequiredException();
        var ct = context.RequestAborted;
        var input = BookingListQuery.Bind(context.Request.Query);
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid) return ApiProblemDetails.Validation(context, validation);
        var (page, pageSize, offset) = input.Parse();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var query = db.Bookings.AsNoTracking().Where(booking => booking.UserId == userId);
        var totalCount = await query.CountAsync(ct);
        var bookings = await query.OrderByDescending(booking => booking.CreatedAt).ThenByDescending(booking => booking.Id)
            .Skip(offset).Take(pageSize).ToArrayAsync(ct);
        var items = bookings.Select(booking =>
        {
            ownership.EnsureBookingOwner(booking);
            return new BookingSummaryResponse(booking.Id, booking.Number, booking.SessionId, (int)booking.Status,
                booking.CreatedAt, booking.SessionStartsAt, booking.PaymentDeadline, booking.CancellationDeadline,
                booking.TotalAmount.Amount, booking.TotalAmount.Currency);
        }).ToArray();
        await transaction.CommitAsync(ct);
        return Results.Ok(new BookingListResponse(items, page, pageSize, totalCount));
    }

    private static async Task<IResult> CreateAsync(CreateBookingRequest request, HoldConversionService conversion,
        HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var bookingId = Guid.NewGuid();
        var booking = await conversion.ConvertAsync(request.HoldId, bookingId, "BK-" + bookingId.ToString("N"),
            context.RequestAborted);
        if (booking is null) return Results.NotFound();
        return Results.Created($"/api/bookings/{booking.Id}", ToResponse(booking, booking.Items));
    }

    private static BookingResponse ToResponse(BookingEntity booking, IReadOnlyList<BookingItem> items) =>
        new(booking.Id, booking.Number, booking.SessionId, (int)booking.Status,
            booking.CreatedAt, booking.SessionStartsAt, booking.PaymentDeadline, booking.CancellationDeadline,
            booking.TotalAmount.Amount, booking.TotalAmount.Currency,
            items.OrderBy(item => item.SessionSeatId).Select(item => new BookingItemResponse(item.SessionSeatId, item.PriceSnapshot.Amount,
                item.PriceSnapshot.Currency)).ToArray());
}
