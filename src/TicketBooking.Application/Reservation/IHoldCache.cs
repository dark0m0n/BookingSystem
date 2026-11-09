using TicketBooking.Contracts.Reservation;

namespace TicketBooking.Application.Reservation;

/// <summary>Best-effort response cache; never authorizes access or inventory changes.</summary>
public interface IHoldCache
{
    /// <summary>Read-only lookup; invalid entries are misses. Mutations belong after the caller's database commit.</summary>
    Task<HoldResponse?> GetAsync(Guid holdId, CancellationToken cancellationToken = default);
    Task SetAsync(HoldResponse hold, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid holdId, CancellationToken cancellationToken = default);
}
