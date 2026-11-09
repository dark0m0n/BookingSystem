using Microsoft.EntityFrameworkCore;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.Api.Reservation;

public sealed record HoldExpiryCursor(DateTimeOffset ExpiresAt, Guid Id);
public sealed record HoldExpiryBatchResult(int Examined, int Expired, HoldExpiryCursor? NextCursor);

/// <summary>Discovers due holds from PostgreSQL; Redis TTLs and notifications never schedule or authorize release.</summary>
public sealed class HoldExpiryBatchProcessor(IServiceScopeFactory scopes, TimeProvider clock,
    HoldExpiryOptions options, ILogger<HoldExpiryBatchProcessor> logger)
{
    public async Task<HoldExpiryBatchResult> ProcessBatchAsync(HoldExpiryCursor? after = null,
        CancellationToken cancellationToken = default)
    {
        List<HoldExpiryCursor> candidates;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var cutoff = clock.GetUtcNow();
            var query = db.SeatHolds.AsNoTracking().Where(hold => hold.Status == SeatHoldStatus.Active && hold.ExpiresAt <= cutoff);
            if (after is not null)
                query = query.Where(hold => hold.ExpiresAt > after.ExpiresAt ||
                    (hold.ExpiresAt == after.ExpiresAt && hold.Id.CompareTo(after.Id) > 0));
            candidates = await query.OrderBy(hold => hold.ExpiresAt).ThenBy(hold => hold.Id).Take(options.BatchSize)
                .Select(hold => new HoldExpiryCursor(hold.ExpiresAt, hold.Id)).ToListAsync(cancellationToken);
        }

        var expired = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A failed transaction never contaminates the next hold's ChangeTracker or transaction.
            await using var scope = scopes.CreateAsyncScope();
            try
            {
                var releaser = scope.ServiceProvider.GetRequiredService<HoldExpiryReleaser>();
                if (await releaser.ExpireAsync(candidate.Id, cancellationToken)) expired++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (SeatAllocationConflictException)
            {
                logger.LogWarning("Hold {HoldId} expiry skipped because its retained allocation no longer matches.", candidate.Id);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Hold {HoldId} expiry failed; a later scan will recheck its stored state.", candidate.Id);
            }
        }

        // Advance past locked/failed rows too, so poison records cannot starve later due holds.
        // After the bounded scan reaches its end, the next poll wraps to the beginning for safe retries.
        return new HoldExpiryBatchResult(candidates.Count, expired,
            candidates.Count == options.BatchSize ? candidates[^1] : null);
    }
}
