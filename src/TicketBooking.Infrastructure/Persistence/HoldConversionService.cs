using Microsoft.EntityFrameworkCore;
using TicketBooking.Application.Identity;
using TicketBooking.Domain;
using TicketBooking.Domain.Catalogue;
using BookingEntity = TicketBooking.Domain.Booking.Booking;

namespace TicketBooking.Infrastructure.Persistence;

/// <summary>Authoritative conversion command. Use a fresh scope and never save a booking/hold conversion separately.</summary>
public sealed class HoldConversionService(TicketBookingDbContext db, SessionSeatAllocationWriter writer,
    ResourceOwnershipService ownership, ICurrentUser currentUser, TimeProvider clock, BookingTimingPolicy timing)
{
    public async Task<BookingEntity?> ConvertAsync(Guid holdId, Guid bookingId, string bookingNumber,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Same hold-first lock as cancellation/expiry. State is loaded only after obtaining the lock.
        var locked = await db.Database.SqlQuery<Guid>(
            $"SELECT \"Id\" AS \"Value\" FROM \"SeatHolds\" WHERE \"Id\" = {holdId} FOR UPDATE").ToListAsync(cancellationToken);
        if (locked.Count == 0) return null;
        var hold = await db.SeatHolds.Include(value => value.Items).SingleAsync(value => value.Id == holdId, cancellationToken);
        ownership.EnsureHoldOwner(hold);
        var userId = currentUser.UserId ?? throw new AuthenticationRequiredException();
        var session = await db.Sessions.FromSqlInterpolated(
            $"SELECT * FROM \"Sessions\" WHERE \"Id\" = {hold.SessionId} FOR SHARE").AsNoTracking().SingleAsync(cancellationToken);
        hold.EnsureCanConvert(userId, clock);
        if (session.Status != SessionStatus.Scheduled || session.StartsAt <= clock.GetUtcNow())
            throw new DomainRuleException(DomainError.SeatNotAvailable, "The session is not bookable.");
        var items = hold.Items.OrderBy(item => item.SessionSeatId).ToArray();
        if (items.Length == 0 || items.Any(item => item.AllocationVersion is null or <= 0 or long.MaxValue))
            throw new SeatAllocationConflictException();
        var ids = items.Select(item => item.SessionSeatId).ToArray();
        var inventory = await db.Database.SqlQuery<Guid>(
            $"SELECT \"Id\" AS \"Value\" FROM \"SessionSeats\" WHERE \"SessionId\" = {hold.SessionId} AND \"Id\" = ANY({ids}) ORDER BY \"Id\" FOR UPDATE")
            .ToListAsync(cancellationToken);
        if (inventory.Count != items.Length) throw new SeatAllocationConflictException();
        // Factory rechecks expiry with fresh time after every lock has been acquired.
        var booking = BookingEntity.CreateFromHold(bookingId, hold, userId, bookingNumber, clock, session.StartsAt, timing);
        var bookingItems = booking.Items.ToDictionary(item => item.SessionSeatId);
        foreach (var item in items)
            bookingItems[item.SessionSeatId].CaptureAllocationVersion(await writer.TransferToBookingAsync(
                item.SessionSeatId, item.AllocationVersion!.Value, cancellationToken));
        hold.Convert(userId, clock);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync(cancellationToken);
        var now = clock.GetUtcNow();
        if (now >= hold.ExpiresAt)
            throw new DomainRuleException(DomainError.HoldExpired, "The hold expired during conversion.");
        if (now >= booking.PaymentDeadline)
            throw new DomainRuleException(DomainError.PaymentDeadlinePassed, "The payment deadline passed during conversion.");
        await transaction.CommitAsync(cancellationToken);
        return booking;
    }
}
