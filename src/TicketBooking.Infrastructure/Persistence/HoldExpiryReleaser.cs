using Microsoft.EntityFrameworkCore;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;

namespace TicketBooking.Infrastructure.Persistence;

/// <summary>One system expiry transaction. Call from a dedicated scope; never refresh claim tokens.</summary>
public sealed class HoldExpiryReleaser(TicketBookingDbContext db, SessionSeatAllocationWriter writer, TimeProvider clock)
{
    public async Task<bool> ExpireAsync(Guid holdId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var locked = await db.Database.SqlQuery<Guid>(
            $"SELECT \"Id\" AS \"Value\" FROM \"SeatHolds\" WHERE \"Id\" = {holdId} AND \"Status\" = 0 FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);
        if (locked.Count == 0) return false;
        var hold = await db.SeatHolds.Include(value => value.Items).SingleAsync(value => value.Id == holdId, cancellationToken);
        if (hold.Status != SeatHoldStatus.Active || hold.ExpiresAt > clock.GetUtcNow()) return false;
        var session = await db.Sessions.FromSqlInterpolated(
            $"SELECT * FROM \"Sessions\" WHERE \"Id\" = {hold.SessionId} FOR SHARE")
            .AsNoTracking().SingleAsync(cancellationToken);
        var items = hold.Items.OrderBy(item => item.SessionSeatId).ToArray();
        if (items.Length == 0 || items.Any(item => item.AllocationVersion is null or <= 0 or long.MaxValue))
            throw new SeatAllocationConflictException();
        var ids = items.Select(item => item.SessionSeatId).ToArray();
        // Acquire all inventory locks in order before deciding whether release can still make seats Available.
        var inventory = await db.Database.SqlQuery<Guid>(
            $"SELECT \"Id\" AS \"Value\" FROM \"SessionSeats\" WHERE \"SessionId\" = {hold.SessionId} AND \"Id\" = ANY({ids}) ORDER BY \"Id\" FOR UPDATE")
            .ToListAsync(cancellationToken);
        if (inventory.Count != items.Length) throw new SeatAllocationConflictException();
        hold.Expire(clock);
        var releasedStatus = session.Status == SessionStatus.Scheduled && session.StartsAt > clock.GetUtcNow()
            ? SessionSeatStatus.Available : SessionSeatStatus.Unavailable;
        foreach (var item in items)
            await writer.ReleaseAsync(item.SessionSeatId, item.AllocationVersion!.Value, SessionSeatStatus.Held,
                releasedStatus, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        // If the session started during processing, roll back and reconsider with fresh time on a later scan.
        if (releasedStatus == SessionSeatStatus.Available && clock.GetUtcNow() >= session.StartsAt) return false;
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
