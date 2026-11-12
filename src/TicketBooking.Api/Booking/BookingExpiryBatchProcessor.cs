using Microsoft.EntityFrameworkCore;
using TicketBooking.Domain.Booking;
using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.Api.Booking;

public sealed record BookingExpiryCursor(DateTimeOffset PaymentDeadline, Guid Id);
public sealed record BookingExpiryBatchResult(int Examined, int Expired, BookingExpiryCursor? NextCursor);

/// <summary>Discovers due bookings from PostgreSQL; Redis TTLs and notifications never schedule or authorize release.</summary>
public sealed class BookingExpiryBatchProcessor(IServiceScopeFactory scopes, TimeProvider clock,
    BookingExpiryOptions options, ILogger<BookingExpiryBatchProcessor> logger)
{
    public async Task<BookingExpiryBatchResult> ProcessBatchAsync(BookingExpiryCursor? after = null,
        CancellationToken cancellationToken = default)
    {
        List<BookingExpiryCursor> candidates;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var cutoff = clock.GetUtcNow();
            var query = db.Bookings.AsNoTracking().Where(booking => booking.Status == BookingStatus.PendingPayment && booking.PaymentDeadline <= cutoff);
            if (after is not null)
                query = query.Where(booking => booking.PaymentDeadline > after.PaymentDeadline ||
                    (booking.PaymentDeadline == after.PaymentDeadline && booking.Id.CompareTo(after.Id) > 0));
            candidates = await query.OrderBy(booking => booking.PaymentDeadline).ThenBy(booking => booking.Id).Take(options.BatchSize)
                .Select(booking => new BookingExpiryCursor(booking.PaymentDeadline, booking.Id)).ToListAsync(cancellationToken);
        }

        var expired = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A failed transaction never contaminates the next booking's ChangeTracker or transaction.
            await using var scope = scopes.CreateAsyncScope();
            try
            {
                var releaser = scope.ServiceProvider.GetRequiredService<BookingExpiryReleaser>();
                if (await releaser.ExpireAsync(candidate.Id, cancellationToken)) expired++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (SeatAllocationConflictException)
            {
                logger.LogWarning("Booking {BookingId} expiry skipped because its retained allocation no longer matches.", candidate.Id);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Booking {BookingId} expiry failed; a later scan will recheck its stored state.", candidate.Id);
            }
        }

        // Advance past locked/failed rows too, so poison records cannot starve later due bookings.
        // After the bounded scan reaches its end, the next poll wraps to the beginning for safe retries.
        return new BookingExpiryBatchResult(candidates.Count, expired,
            candidates.Count == options.BatchSize ? candidates[^1] : null);
    }
}
