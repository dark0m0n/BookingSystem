using Microsoft.EntityFrameworkCore;
using TicketBooking.Domain.Catalogue;

namespace TicketBooking.Infrastructure.Persistence;

/// <summary>
/// Fenced inventory writes. Callers must validate ownership, deadlines, and business state,
/// and coordinate these writes with hold/booking changes in the same database transaction.
/// </summary>
public sealed class SessionSeatAllocationWriter(TicketBookingDbContext context)
{
    public Task<long> HoldAsync(Guid sessionSeatId, long expectedVersion, CancellationToken cancellationToken = default) =>
        WriteAsync(sessionSeatId, expectedVersion, SessionSeatStatus.Available, SessionSeatStatus.Held, cancellationToken);

    public Task<long> TransferToBookingAsync(Guid sessionSeatId, long expectedVersion, CancellationToken cancellationToken = default) =>
        // Held -> Held must still advance the fencing token when allocation ownership changes.
        WriteAsync(sessionSeatId, expectedVersion, SessionSeatStatus.Held, SessionSeatStatus.Held, cancellationToken);

    public Task<long> ConfirmAsync(Guid sessionSeatId, long expectedVersion, CancellationToken cancellationToken = default) =>
        WriteAsync(sessionSeatId, expectedVersion, SessionSeatStatus.Held, SessionSeatStatus.Booked, cancellationToken);

    public Task<long> ReleaseAsync(
        Guid sessionSeatId,
        long expectedVersion,
        SessionSeatStatus expectedStatus,
        SessionSeatStatus releasedStatus,
        CancellationToken cancellationToken = default)
    {
        if (expectedStatus is not (SessionSeatStatus.Held or SessionSeatStatus.Booked))
        {
            throw new ArgumentOutOfRangeException(nameof(expectedStatus), "Only an allocated seat can be released.");
        }

        if (releasedStatus is not (SessionSeatStatus.Available or SessionSeatStatus.Unavailable))
        {
            throw new ArgumentOutOfRangeException(nameof(releasedStatus), "Released inventory must be available or unavailable.");
        }

        return WriteAsync(sessionSeatId, expectedVersion, expectedStatus, releasedStatus, cancellationToken);
    }

    private async Task<long> WriteAsync(
        Guid sessionSeatId,
        long expectedVersion,
        SessionSeatStatus expectedStatus,
        SessionSeatStatus newStatus,
        CancellationToken cancellationToken)
    {
        if (sessionSeatId == Guid.Empty)
        {
            throw new ArgumentException("A session seat identifier is required.", nameof(sessionSeatId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(expectedVersion);
        var nextVersion = checked(expectedVersion + 1);
        var affected = await context.SessionSeats
            .Where(seat => seat.Id == sessionSeatId && seat.Version == expectedVersion && seat.Status == expectedStatus)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(seat => seat.Status, newStatus)
                .SetProperty(seat => seat.Version, nextVersion), cancellationToken);

        if (affected != 1)
        {
            throw new SeatAllocationConflictException();
        }

        return nextVersion;
    }
}
