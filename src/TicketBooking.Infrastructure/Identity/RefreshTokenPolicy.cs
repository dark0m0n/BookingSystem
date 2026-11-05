namespace TicketBooking.Infrastructure.Identity;

public sealed class RefreshTokenPolicy
{
    public RefreshTokenPolicy(TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromDays(90))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Refresh lifetime must be positive and at most 90 days.");
        }
        Lifetime = lifetime;
    }

    public TimeSpan Lifetime { get; }
}
