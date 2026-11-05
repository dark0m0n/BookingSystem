using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Auth;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresTheory]
    [InlineData("missing@example.test", Password)]
    [InlineData("alice@example.test", "wrong-password")]
    public async Task InvalidCredentials_NeverIssueTokensOrPersistRefreshSessions(string email, string password)
    {
        using var registered = await Register();

        using var response = await Login(email, password);

        var problem = await AssertProblem(response, 401, "invalid_credentials");
        Assert.False(problem.TryGetProperty("accessToken", out _));
        Assert.False(problem.TryGetProperty("refreshToken", out _));
        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.Equal(1, await database.Users.CountAsync());
        Assert.Empty(await database.RefreshSessions.ToArrayAsync());
        Assert.Empty(await database.RefreshTokens.ToArrayAsync());
    }

    [PostgresFact]
    public async Task RefreshAndLogout_AreBoundToTheTokenOwnerAndLeaveOtherUsersUnaffected()
    {
        using var registeredAlice = await Register();
        using var registeredBob = await Register("bob@example.test", "Bob");
        Assert.Equal(HttpStatusCode.Created, registeredBob.StatusCode);
        var alice = await LoginToken();
        using var bobLogin = await Login("bob@example.test", Password);
        Assert.Equal(HttpStatusCode.OK, bobLogin.StatusCode);
        var bob = (await bobLogin.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.NotEqual(alice.User.Id, bob.User.Id);

        var rotatedBob = await RotateToken(bob.RefreshToken);

        Assert.Equal(bob.User, rotatedBob.User);
        Assert.Equal(bob.User.Id.ToString(), new JwtSecurityTokenHandler().ReadJwtToken(rotatedBob.AccessToken).Subject);
        using var protectedBob = await Protected(rotatedBob.AccessToken);
        Assert.Equal(HttpStatusCode.OK, protectedBob.StatusCode);
        Assert.Equal(bob.User.Id.ToString(), (await protectedBob.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("subject").GetString());

        using var logoutBob = await Logout(bob.RefreshToken);
        await AssertLoggedOut(logoutBob);
        using var rejectedBob = await Refresh(rotatedBob.RefreshToken);
        await AssertProblem(rejectedBob, 401, "invalid_refresh_token");
        var rotatedAlice = await RotateToken(alice.RefreshToken);
        Assert.Equal(alice.User, rotatedAlice.User);
        using var protectedAlice = await Protected(rotatedAlice.AccessToken);
        Assert.Equal(HttpStatusCode.OK, protectedAlice.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var sessions = await database.RefreshSessions.AsNoTracking().ToArrayAsync();
        Assert.Equal(2, sessions.Length);
        Assert.NotNull(Assert.Single(sessions, session => session.UserId == bob.User.Id).RevokedAt);
        Assert.Null(Assert.Single(sessions, session => session.UserId == alice.User.Id).RevokedAt);
    }
}
