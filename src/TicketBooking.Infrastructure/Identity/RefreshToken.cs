namespace TicketBooking.Infrastructure.Identity;

public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    internal RefreshToken(Guid sessionId, string tokenHash, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        SessionId = sessionId;
        TokenHash = tokenHash;
        CreatedAt = createdAt.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
}
