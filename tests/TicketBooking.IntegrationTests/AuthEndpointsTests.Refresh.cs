using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Auth;
using TicketBooking.Infrastructure.Identity;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresFact]
    public async Task Login_PersistsOnlyRefreshHashesAndAnAbsoluteUtcExpiry()
    {
        using var registered = await Register();
        var login = await LoginToken();

        Assert.True(RefreshTokenService.IsWellFormed(login.RefreshToken));
        Assert.InRange(clock.GetUtcNow().AddDays(7) - login.RefreshTokenExpiresAt,
            TimeSpan.Zero, TimeSpan.FromTicks(TimeSpan.TicksPerMicrosecond - 1));
        Assert.Equal(0, login.RefreshTokenExpiresAt.Ticks % TimeSpan.TicksPerMicrosecond);
        Assert.Equal(TimeSpan.Zero, login.RefreshTokenExpiresAt.Offset);
        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var token = await context.RefreshTokens.AsNoTracking().SingleAsync();
        var session = await context.RefreshSessions.AsNoTracking().SingleAsync();
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(login.RefreshToken))), token.TokenHash);
        Assert.Equal(64, token.TokenHash.Length);
        Assert.NotEqual(login.RefreshToken, token.TokenHash);
        Assert.DoesNotContain(login.RefreshToken, JsonSerializer.Serialize(token));
        Assert.DoesNotContain(login.RefreshToken, JsonSerializer.Serialize(session));
        Assert.Equal(session.Id, token.SessionId);
        Assert.Equal(login.User.Id, session.UserId);
        Assert.Equal(login.RefreshTokenExpiresAt, session.ExpiresAt);
        Assert.Null(token.ConsumedAt);
        Assert.Null(session.RevokedAt);
    }

    [PostgresFact]
    public async Task Refresh_AfterAccessExpiry_ReturnsNewTokensWithoutExtendingTheFamily()
    {
        using var registered = await Register();
        var login = await LoginToken();
        clock.Set(login.ExpiresAt);
        using var expired = await Protected(login.AccessToken);
        await AssertProblem(expired, 401, "unauthorized");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
        {
            Content = JsonContent.Create(new RefreshRequest(login.RefreshToken))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Contains(response.Headers.Pragma, value => value.Name == "no-cache");
        var refreshed = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.NotEqual(login.AccessToken, refreshed.AccessToken);
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
        Assert.Equal(login.User, refreshed.User);
        Assert.Equal(login.RefreshTokenExpiresAt, refreshed.RefreshTokenExpiresAt);
        Assert.Equal(clock.GetUtcNow().AddMinutes(15), refreshed.ExpiresAt);
        using var valid = await Protected(refreshed.AccessToken);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var tokens = await scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().RefreshTokens.AsNoTracking().ToArrayAsync();
        Assert.Equal(2, tokens.Length);
        Assert.Single(tokens, token => token.ConsumedAt == clock.GetUtcNow());
        Assert.Single(tokens, token => token.ConsumedAt is null);
    }

    [PostgresFact]
    public async Task ReplayingAnAncestor_RevokesItsDescendantsButNotAnotherLoginFamily()
    {
        using var registered = await Register();
        var first = await LoginToken();
        var independent = await LoginToken();
        var second = await RotateToken(first.RefreshToken);
        var third = await RotateToken(second.RefreshToken);

        using var replay = await Refresh(first.RefreshToken);
        await AssertProblem(replay, 401, "invalid_refresh_token");
        Assert.DoesNotContain(first.RefreshToken, await replay.Content.ReadAsStringAsync());
        using var descendant = await Refresh(third.RefreshToken);
        await AssertProblem(descendant, 401, "invalid_refresh_token");
        var other = await RotateToken(independent.RefreshToken);
        Assert.Equal(independent.RefreshTokenExpiresAt, other.RefreshTokenExpiresAt);
        // Refresh-family revocation does not revoke an already issued, unexpired JWT.
        using var existingAccess = await Protected(third.AccessToken);
        Assert.Equal(HttpStatusCode.OK, existingAccess.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().RefreshSessions
            .Where(session => session.RevokedAt != null).ToArrayAsync());
    }

    [PostgresFact]
    public async Task ConcurrentRefreshes_CommitOneReplacementAndThenRevokeTheReplayedFamily()
    {
        using var registered = await Register();
        var login = await LoginToken();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Refresh(login.RefreshToken)));
        try
        {
            var success = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            var replacement = (await success.Content.ReadFromJsonAsync<LoginResponse>())!;
            foreach (var failed in responses.Where(response => response != success))
            {
                await AssertProblem(failed, 401, "invalid_refresh_token");
            }
            using var revoked = await Refresh(replacement.RefreshToken);
            await AssertProblem(revoked, 401, "invalid_refresh_token");
            await using var scope = app.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            Assert.Equal(2, await context.RefreshTokens.CountAsync());
            Assert.Single(await context.RefreshTokens.Where(token => token.ConsumedAt == null).ToArrayAsync());
            Assert.NotNull((await context.RefreshSessions.SingleAsync()).RevokedAt);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [PostgresFact]
    public async Task AncestorReplayRacingWithDescendantRotation_CannotLeaveAUsableFamily()
    {
        using var registered = await Register();
        var first = await LoginToken();
        var second = await RotateToken(first.RefreshToken);
        var responses = await Task.WhenAll(Refresh(first.RefreshToken), Refresh(second.RefreshToken));
        try
        {
            await AssertProblem(responses[0], 401, "invalid_refresh_token");
            if (responses[1].StatusCode == HttpStatusCode.OK)
            {
                var third = (await responses[1].Content.ReadFromJsonAsync<LoginResponse>())!;
                using var revoked = await Refresh(third.RefreshToken);
                await AssertProblem(revoked, 401, "invalid_refresh_token");
            }
            else
            {
                await AssertProblem(responses[1], 401, "invalid_refresh_token");
            }
            await using var scope = app.Services.CreateAsyncScope();
            Assert.NotNull((await scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().RefreshSessions.SingleAsync()).RevokedAt);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [PostgresTheory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task RefreshExpiry_EnforcesTheAbsoluteCutoff(long ticksFromExpiry, bool allowed)
    {
        using var registered = await Register();
        var login = await LoginToken();
        clock.Set(login.RefreshTokenExpiresAt.AddTicks(ticksFromExpiry));

        using var response = await Refresh(login.RefreshToken);

        if (allowed)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(login.RefreshTokenExpiresAt, (await response.Content.ReadFromJsonAsync<LoginResponse>())!.RefreshTokenExpiresAt);
        }
        else
        {
            await AssertProblem(response, 401, "invalid_refresh_token");
            await using var scope = app.Services.CreateAsyncScope();
            Assert.Null((await scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().RefreshTokens.SingleAsync()).ConsumedAt);
        }
    }

    [PostgresFact]
    public async Task PersistedRefreshToken_CanRotateUsingAnIndependentServiceProvider()
    {
        using var registered = await Register();
        var login = await LoginToken();
        // Use original fixture settings; an opened data source redacts its connection-string password.
        await using var independent = IdentityConfigurationTests.CreateServices(testConnection);
        await using var independentScope = independent.CreateAsyncScope();
        var provider = independentScope.ServiceProvider;
        var service = new RefreshTokenService(provider.GetRequiredService<TicketBookingDbContext>(),
            provider.GetRequiredService<UserManager<ApplicationUser>>(), provider.GetRequiredService<IUserConfirmation<ApplicationUser>>(),
            new RefreshTokenPolicy(TimeSpan.FromDays(7)), clock);

        var rotated = await service.RotateAsync(login.RefreshToken);

        Assert.NotNull(rotated);
        Assert.Equal(login.User.Id, rotated.User.Id);
        Assert.Equal(login.RefreshTokenExpiresAt, rotated.ExpiresAt);
        Assert.NotEqual(login.RefreshToken, rotated.Token);
        await RotateToken(rotated.Token);
    }

    [PostgresTheory]
    [InlineData("security-stamp")]
    [InlineData("lockout")]
    [InlineData("two-factor")]
    public async Task ChangedAccountEligibility_RevokesRefreshFamily(string change)
    {
        using var registered = await Register();
        var login = await LoginToken();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(login.User.Id.ToString()))!;
            var result = change switch
            {
                "security-stamp" => await users.UpdateSecurityStampAsync(user),
                "lockout" => await users.SetLockoutEndDateAsync(user, clock.GetUtcNow().AddMinutes(5)),
                _ => await users.SetTwoFactorEnabledAsync(user, true)
            };
            Assert.True(result.Succeeded);
        }

        using var response = await Refresh(login.RefreshToken);

        await AssertProblem(response, 401, "invalid_refresh_token");
        await using var check = app.Services.CreateAsyncScope();
        Assert.NotNull((await check.ServiceProvider.GetRequiredService<TicketBookingDbContext>().RefreshSessions.SingleAsync()).RevokedAt);
    }

    [PostgresFact]
    public async Task DeletedAccount_CascadesRefreshHistoryAndCannotRefresh()
    {
        using var registered = await Register();
        var login = await LoginToken();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.DeleteAsync((await users.FindByIdAsync(login.User.Id.ToString()))!)).Succeeded);
        }
        using var response = await Refresh(login.RefreshToken);
        await AssertProblem(response, 401, "invalid_refresh_token");
        await using var check = app.Services.CreateAsyncScope();
        var context = check.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Empty(await context.RefreshTokens.ToArrayAsync());
        Assert.Empty(await context.RefreshSessions.ToArrayAsync());
    }

    [PostgresFact]
    public async Task Refresh_UsesCurrentProfileAndServerRoleAssignments()
    {
        using var registered = await Register();
        var login = await LoginToken();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var user = (await users.FindByIdAsync(login.User.Id.ToString()))!;
            user.DisplayName = "Updated name";
            Assert.True((await users.UpdateAsync(user)).Succeeded);
            Assert.True(await roles.RoleExistsAsync("Admin"));
            Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
        }

        var refreshed = await RotateToken(login.RefreshToken);

        Assert.Equal("Updated name", refreshed.User.DisplayName);
        using var allowed = await Admin(refreshed.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
    }

    [PostgresFact]
    public async Task FailedReplacementInsert_RollsBackConsumptionAndAllowsTheOriginalToken()
    {
        using var registered = await Register();
        var login = await LoginToken();
        refreshWriteFailure.FailNext();

        using var failed = await Refresh(login.RefreshToken);

        await AssertProblem(failed, 500, "internal_error");
        Assert.DoesNotContain(login.RefreshToken, await failed.Content.ReadAsStringAsync());
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            Assert.Null((await context.RefreshTokens.SingleAsync()).ConsumedAt);
            Assert.Null((await context.RefreshSessions.SingleAsync()).RevokedAt);
        }
        await RotateToken(login.RefreshToken);
    }

    [PostgresTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-refresh-token")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB")]
    public async Task InvalidRefreshInput_ReturnsValidationWithoutEchoingTheSecret(string? secret)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(secret));

        var problem = await AssertProblem(response, 400, "validation_failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty("refreshToken", out _));
    }

    [PostgresFact]
    public async Task UnknownRefreshToken_DoesNotRevokeAnotherSession()
    {
        using var registered = await Register();
        var login = await LoginToken();
        var unknown = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var response = await Refresh(unknown);

        await AssertProblem(response, 401, "invalid_refresh_token");
        Assert.DoesNotContain(unknown, await response.Content.ReadAsStringAsync());
        await RotateToken(login.RefreshToken);
    }

    private Task<HttpResponseMessage> Refresh(string secret) =>
        client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(secret));

    private async Task<LoginResponse> RotateToken(string secret)
    {
        using var response = await Refresh(secret);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private sealed class RefreshWriteFailure : SaveChangesInterceptor
    {
        private int failNext;
        public void FailNext() => Interlocked.Exchange(ref failNext, 1);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<RefreshToken>().Any(entry => entry.State == EntityState.Added) &&
                Interlocked.Exchange(ref failNext, 0) == 1)
            {
                throw new InvalidOperationException("Test-only refresh replacement failure.");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
