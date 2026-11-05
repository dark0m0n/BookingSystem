namespace TicketBooking.Infrastructure.Identity;

public sealed class RefreshSession
{
    private RefreshSession()
    {
    }

    internal RefreshSession(Guid userId, string securityStampHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        SecurityStampHash = securityStampHash;
        CreatedAt = createdAt.ToUniversalTime();
        ExpiresAt = expiresAt.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string SecurityStampHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
}
