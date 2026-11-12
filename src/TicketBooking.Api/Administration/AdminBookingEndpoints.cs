using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TicketBooking.Api.Auth;
using TicketBooking.Api.Errors;
using TicketBooking.Api.Validation;
using TicketBooking.Contracts.Administration;
using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.Api.Administration;

public static class AdminBookingEndpoints
{
    public static IEndpointRouteBuilder MapAdminBookingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/bookings", GetAsync)
            .RequireAuthorization(AuthorizationPolicies.Admin).WithTags("Administration")
            .Produces<AdminBookingListResponse>().ProducesValidationProblem()
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(500);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(TicketBookingDbContext db, IValidator<BookingListQuery> validator,
        HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var ct = context.RequestAborted;
        var input = BookingListQuery.Bind(context.Request.Query);
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid) return ApiProblemDetails.Validation(context, validation);
        var (page, pageSize, offset) = input.Parse();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        // This explicit Admin workflow lists all owners; customer routes retain ownership checks.
        var query = db.Bookings.AsNoTracking();
        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(booking => booking.CreatedAt).ThenByDescending(booking => booking.Id)
            .Skip(offset).Take(pageSize)
            .Select(booking => new AdminBookingSummaryResponse(booking.Id, booking.Number, booking.UserId,
                booking.SessionId, (int)booking.Status, booking.CreatedAt, booking.SessionStartsAt,
                booking.PaymentDeadline, booking.CancellationDeadline, booking.TotalAmount.Amount,
                booking.TotalAmount.Currency)).ToArrayAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(new AdminBookingListResponse(items, page, pageSize, totalCount));
    }
}
