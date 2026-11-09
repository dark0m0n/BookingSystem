namespace TicketBooking.Infrastructure.Caching;

public sealed class SeatAvailabilityCacheOptions
{
    public const string SectionName = "SeatAvailabilityCache";
    public bool Enabled { get; set; } = true;
    public int MinimumSeatCount { get; set; } = 64;
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromSeconds(5);

    public void Validate()
    {
        if (MinimumSeatCount < 1)
            throw new InvalidOperationException("SeatAvailabilityCache:MinimumSeatCount must be positive.");
        if (Lifetime < TimeSpan.FromSeconds(1) || Lifetime > TimeSpan.FromSeconds(30))
            throw new InvalidOperationException("SeatAvailabilityCache:Lifetime must be between one and thirty seconds.");
    }
}
