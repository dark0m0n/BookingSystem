using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Auth;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    private readonly LogoutCommitFailure logoutCommitFailure = new();

    [PostgresFact]
    public async Task Logout_RevokesRenewalButPreservesHistoryAndExistingAccessExpiry()
    {
        using var registered = await Register();
        var login = await LoginToken();

        using var response = await Logout(login.RefreshToken);

        await AssertLoggedOut(response);
        using var refresh = await Refresh(login.RefreshToken);
        await AssertProblem(refresh, 401, "invalid_refresh_token");
        Assert.DoesNotContain(login.RefreshToken, await refresh.Content.ReadAsStringAsync());
        using var access = await Protected(login.AccessToken);
        Assert.Equal(HttpStatusCode.OK, access.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.NotNull((await context.RefreshSessions.SingleAsync()).RevokedAt);
        Assert.Null((await context.RefreshTokens.SingleAsync()).ConsumedAt);
    }

    [PostgresFact]
    public async Task Logout_WithConsumedAncestor_RevokesDescendantsAndPreservesOtherFamilies()
    {
        using var registered = await Register();
        var original = await LoginToken();
        var independent = await LoginToken();
        var child = await RotateToken(original.RefreshToken);
        var grandchild = await RotateToken(child.RefreshToken);

        using var response = await Logout(original.RefreshToken);

        await AssertLoggedOut(response);
        using var denied = await Refresh(grandchild.RefreshToken);
        await AssertProblem(denied, 401, "invalid_refresh_token");
        await RotateToken(independent.RefreshToken);
        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Equal(2, await context.RefreshSessions.CountAsync());
        Assert.Single(await context.RefreshSessions.Where(session => session.RevokedAt != null).ToArrayAsync());
    }

    [PostgresFact]
    public async Task Logout_IsIdempotentAndPreservesTheFirstRevocationTime()
    {
        using var registered = await Register();
        var login = await LoginToken();
        using var first = await Logout(login.RefreshToken);
        await AssertLoggedOut(first);
        DateTimeOffset revokedAt;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            revokedAt = (await scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>()
                .RefreshSessions.SingleAsync()).RevokedAt!.Value;
        }
        clock.Set(clock.GetUtcNow().AddHours(1));

        using var repeated = await Logout(login.RefreshToken);

        await AssertLoggedOut(repeated);
        using var reuse = await Refresh(login.RefreshToken);
        await AssertProblem(reuse, 401, "invalid_refresh_token");
        await using var check = app.Services.CreateAsyncScope();
        Assert.Equal(revokedAt, (await check.ServiceProvider.GetRequiredService<TicketBookingDbContext>()
            .RefreshSessions.SingleAsync()).RevokedAt);
    }

    [PostgresFact]
    public async Task Logout_OfUnknownTokenReturnsNoContentWithoutChangingAnotherFamily()
    {
        using var registered = await Register();
        var login = await LoginToken();
        var unknown = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        using var response = await Logout(unknown);

        await AssertLoggedOut(response);
        await RotateToken(login.RefreshToken);
    }

    [PostgresFact]
    public async Task Logout_WithExpiredAccessHeaderStillRevokesItsRefreshFamily()
    {
        using var registered = await Register();
        var login = await LoginToken();
        clock.Set(login.ExpiresAt);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new LogoutRequest(login.RefreshToken))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        using var response = await client.SendAsync(request);

        await AssertLoggedOut(response);
        using var denied = await Refresh(login.RefreshToken);
        await AssertProblem(denied, 401, "invalid_refresh_token");
    }

    [PostgresFact]
    public async Task Logout_OfExpiredFamilyReturnsNoContentAndRecordsRevocation()
    {
        using var registered = await Register();
        var login = await LoginToken();
        clock.Set(login.RefreshTokenExpiresAt.AddDays(1));

        using var response = await Logout(login.RefreshToken);

        await AssertLoggedOut(response);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.NotNull((await scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().RefreshSessions.SingleAsync()).RevokedAt);
    }

    [PostgresFact]
    public async Task Logout_AfterReplayRevocationIsAnIdempotentSuccess()
    {
        using var registered = await Register();
        var login = await LoginToken();
        var rotated = await RotateToken(login.RefreshToken);
        using var replay = await Refresh(login.RefreshToken);
        await AssertProblem(replay, 401, "invalid_refresh_token");

        using var response = await Logout(rotated.RefreshToken);

        await AssertLoggedOut(response);
        using var denied = await Refresh(rotated.RefreshToken);
        await AssertProblem(denied, 401, "invalid_refresh_token");
    }

    [PostgresFact]
    public async Task LogoutRacingWithRotation_CannotLeaveAUsableReplacement()
    {
        using var registered = await Register();
        var login = await LoginToken();
        var responses = await Task.WhenAll(Refresh(login.RefreshToken), Logout(login.RefreshToken));
        try
        {
            await AssertLoggedOut(responses[1]);
            if (responses[0].StatusCode == HttpStatusCode.OK)
            {
                var replacement = (await responses[0].Content.ReadFromJsonAsync<LoginResponse>())!;
                using var denied = await Refresh(replacement.RefreshToken);
                await AssertProblem(denied, 401, "invalid_refresh_token");
            }
            else
            {
                await AssertProblem(responses[0], 401, "invalid_refresh_token");
            }
            using var original = await Refresh(login.RefreshToken);
            await AssertProblem(original, 401, "invalid_refresh_token");
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

    [PostgresFact]
    public async Task AncestorLogoutRacingWithDescendantRotation_RevokesEveryGeneration()
    {
        using var registered = await Register();
        var original = await LoginToken();
        var child = await RotateToken(original.RefreshToken);
        var responses = await Task.WhenAll(Logout(original.RefreshToken), Refresh(child.RefreshToken));
        try
        {
            await AssertLoggedOut(responses[0]);
            if (responses[1].StatusCode == HttpStatusCode.OK)
            {
                var grandchild = (await responses[1].Content.ReadFromJsonAsync<LoginResponse>())!;
                using var denied = await Refresh(grandchild.RefreshToken);
                await AssertProblem(denied, 401, "invalid_refresh_token");
            }
            else
            {
                await AssertProblem(responses[1], 401, "invalid_refresh_token");
            }
            using var childAgain = await Refresh(child.RefreshToken);
            await AssertProblem(childAgain, 401, "invalid_refresh_token");
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
    public async Task ConcurrentLogouts_AllSucceedWithoutCreatingOrDeletingTokenHistory()
    {
        using var registered = await Register();
        var login = await LoginToken();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Logout(login.RefreshToken)));
        try
        {
            foreach (var response in responses)
            {
                await AssertLoggedOut(response);
            }
            await using var scope = app.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            Assert.Equal(1, await context.RefreshSessions.CountAsync());
            Assert.Equal(1, await context.RefreshTokens.CountAsync());
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
    public async Task FailedLogoutCommit_RollsBackRevocationAndCanBeRetried()
    {
        using var registered = await Register();
        var login = await LoginToken();
        logoutCommitFailure.FailNext();

        using var failed = await Logout(login.RefreshToken);

        await AssertProblem(failed, 500, "internal_error");
        Assert.DoesNotContain(login.RefreshToken, await failed.Content.ReadAsStringAsync());
        await using (var scope = app.Services.CreateAsyncScope())
        {
            Assert.Null((await scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().RefreshSessions.SingleAsync()).RevokedAt);
        }
        var stillUsable = await RotateToken(login.RefreshToken);
        using var retry = await Logout(login.RefreshToken);
        await AssertLoggedOut(retry);
        using var denied = await Refresh(stillUsable.RefreshToken);
        await AssertProblem(denied, 401, "invalid_refresh_token");
    }

    [PostgresTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-refresh-token")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB")]
    public async Task Logout_RejectsMalformedInputWithoutRevokingAValidFamily(string? secret)
    {
        using var registered = await Register();
        var login = await LoginToken();
        using var response = await client.PostAsJsonAsync("/api/auth/logout", new LogoutRequest(secret));

        var problem = await AssertProblem(response, 400, "validation_failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty("refreshToken", out _));
        await RotateToken(login.RefreshToken);
    }

    private Task<HttpResponseMessage> Logout(string secret) =>
        client.PostAsJsonAsync("/api/auth/logout", new LogoutRequest(secret));

    private static async Task AssertLoggedOut(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Contains(response.Headers.Pragma, value => value.Name == "no-cache");
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    private sealed class LogoutCommitFailure : DbTransactionInterceptor
    {
        private int failNext;
        public void FailNext() => Interlocked.Exchange(ref failNext, 1);

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref failNext, 0) == 1)
            {
                throw new InvalidOperationException("Test-only logout commit failure.");
            }
            return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }
}
