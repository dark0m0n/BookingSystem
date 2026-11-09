using TicketBooking.Contracts.Catalogue;

namespace TicketBooking.Application.Catalogue;

public sealed record SeatAvailabilityCacheKey(Guid SessionId, Guid AuditoriumId, string Revision);

/// <summary>Advisory seat maps only. Inventory revisions and allocation decisions come from PostgreSQL.</summary>
public interface ISeatAvailabilityCache
{
    bool IsUseful(int seatCount);
    Task<SessionSeatMapResponse?> GetAsync(SeatAvailabilityCacheKey key, CancellationToken cancellationToken = default);
    Task SetAsync(SeatAvailabilityCacheKey key, SessionSeatMapResponse map, CancellationToken cancellationToken = default);
}
