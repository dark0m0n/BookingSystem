using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.Infrastructure.Identity;

public sealed class RefreshTokenService(TicketBookingDbContext context, UserManager<ApplicationUser> users,
    IUserConfirmation<ApplicationUser> confirmation, RefreshTokenPolicy policy, TimeProvider clock)
{
    public async Task<RefreshTokenGrant> CreateAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(user.SecurityStamp))
        {
            throw new InvalidOperationException("Account security state is unavailable.");
        }
        var roles = await users.GetRolesAsync(user);
        var now = clock.GetUtcNow().ToUniversalTime();
        var expiresAt = now.Add(policy.Lifetime);
        // Match PostgreSQL timestamp precision before exposing this immutable deadline to clients.
        expiresAt = expiresAt.AddTicks(-(expiresAt.Ticks % TimeSpan.TicksPerMicrosecond));
        var session = new RefreshSession(user.Id, Hash(user.SecurityStamp), now, expiresAt);
        var secret = NewSecret();
        context.RefreshSessions.Add(session);
        context.RefreshTokens.Add(new RefreshToken(session.Id, Hash(secret), now));
        await context.SaveChangesAsync(cancellationToken);
        return new RefreshTokenGrant(secret, session.ExpiresAt, user, roles.ToArray());
    }

    public async Task<RefreshTokenGrant?> RotateAsync(string secret, CancellationToken cancellationToken = default)
    {
        if (!IsWellFormed(secret))
        {
            return null;
        }
        var hash = Hash(secret);
        var candidate = await context.RefreshTokens.AsNoTracking()
            .Where(token => token.TokenHash == hash).Select(token => new { token.Id, token.SessionId })
            .SingleOrDefaultAsync(cancellationToken);
        if (candidate is null)
        {
            return null;
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // Every generation locks the same family row, including replay/revocation requests.
        var session = await LockSessionAsync(candidate.SessionId, cancellationToken);
        // Sample after acquiring the lock; time spent waiting cannot extend eligibility.
        var now = clock.GetUtcNow().ToUniversalTime();
        if (session is null || session.RevokedAt is not null || now >= session.ExpiresAt)
        {
            return null;
        }
        var token = await context.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(value => value.Id == candidate.Id, cancellationToken);
        if (token is null)
        {
            return null;
        }
        var user = await context.Users.AsNoTracking().SingleOrDefaultAsync(value => value.Id == session.UserId, cancellationToken);
        if (token.ConsumedAt is not null || !await CanRefreshAsync(user, session, now))
        {
            await RevokeFamilyAsync(session.Id, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var roles = await users.GetRolesAsync(user!);
        var replacement = NewSecret();
        // Execute before the insert so the unique unconsumed-token index is never violated.
        var affected = await context.RefreshTokens.Where(value => value.Id == token.Id && value.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.ConsumedAt, now), cancellationToken);
        if (affected != 1)
        {
            throw new DbUpdateConcurrencyException("The refresh token changed during rotation.");
        }
        context.RefreshTokens.Add(new RefreshToken(session.Id, Hash(replacement), now));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RefreshTokenGrant(replacement, session.ExpiresAt, user!, roles.ToArray());
    }

    public async Task RevokeAsync(string secret, CancellationToken cancellationToken = default)
    {
        if (!IsWellFormed(secret))
        {
            return;
        }
        var hash = Hash(secret);
        var sessionId = await context.RefreshTokens.AsNoTracking().Where(token => token.TokenHash == hash)
            .Select(token => (Guid?)token.SessionId).SingleOrDefaultAsync(cancellationToken);
        if (sessionId is null)
        {
            return;
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var session = await LockSessionAsync(sessionId.Value, cancellationToken);
        // Possession of any generation, including a consumed ancestor, can revoke this family.
        if (session is not null && session.RevokedAt is null)
        {
            await RevokeFamilyAsync(session.Id, clock.GetUtcNow().ToUniversalTime(), cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private Task<RefreshSession?> LockSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        context.RefreshSessions.FromSqlInterpolated(
                $"SELECT * FROM \"RefreshSessions\" WHERE \"Id\" = {sessionId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    private Task<int> RevokeFamilyAsync(Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken) =>
        context.RefreshSessions.Where(session => session.Id == sessionId && session.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.RevokedAt, now), cancellationToken);

    public static bool IsWellFormed(string? secret)
    {
        if (secret is null || secret.Length != 43 || secret.Any(character =>
            !(character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
        {
            return false;
        }
        var bytes = Convert.FromBase64String(secret.Replace('-', '+').Replace('_', '/') + "=");
        return Encode(bytes) == secret;
    }

    private async Task<bool> CanRefreshAsync(ApplicationUser? user, RefreshSession session, DateTimeOffset now)
    {
        if (user is null || string.IsNullOrEmpty(user.Email) || string.IsNullOrEmpty(user.SecurityStamp) ||
            !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(session.SecurityStampHash),
                SHA256.HashData(Encoding.UTF8.GetBytes(user.SecurityStamp))) || user.TwoFactorEnabled ||
            (user.LockoutEnabled && user.LockoutEnd > now))
        {
            return false;
        }
        var signIn = users.Options.SignIn;
        return (!signIn.RequireConfirmedEmail || user.EmailConfirmed) &&
            (!signIn.RequireConfirmedPhoneNumber || user.PhoneNumberConfirmed) &&
            (!signIn.RequireConfirmedAccount || await confirmation.IsConfirmedAsync(users, user));
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string NewSecret() => Encode(RandomNumberGenerator.GetBytes(32));
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
