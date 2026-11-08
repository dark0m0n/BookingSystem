using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using TicketBooking.Api.Administration;
using TicketBooking.Api.Auth;
using TicketBooking.Api.Catalogue;
using TicketBooking.Api.Configuration;
using TicketBooking.Api.Errors;
using TicketBooking.Api.Reservation;
using TicketBooking.Api.Validation;
using TicketBooking.Contracts.Auth;
using TicketBooking.Infrastructure.Identity;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests : IAsyncLifetime
{
    private const string Password = "Auth-test-Password1!";
    private const string SigningKey = "auth-test-only-signing-key-with-at-least-32-bytes";
    private readonly string schema = "auth_tests_" + Guid.NewGuid().ToString("N");
    private readonly string adminConnection = Environment.GetEnvironmentVariable("TICKETBOOKING_TEST_CONNECTION_STRING")!;
    private string testConnection = null!;
    private readonly TestClock clock = new();
    private readonly RefreshWriteFailure refreshWriteFailure = new();
    private WebApplication app = null!;
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        await ExecuteSchemaCommand($"CREATE SCHEMA \"{schema}\"");
        testConnection = new NpgsqlConnectionStringBuilder(adminConnection) { SearchPath = schema }.ConnectionString;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production, Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "auth-tests",
            ["Jwt:Audience"] = "auth-test-client",
            ["Jwt:SigningKey"] = SigningKey,
            ["Jwt:AccessTokenMinutes"] = "15"
        });
        builder.Services.AddSingleton<TimeProvider>(clock);
        // Own this schema's pool so disposing the test host closes its idle connections.
        builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(testConnection));
        builder.Services.AddDbContext<TicketBookingDbContext>((services, options) =>
            options.UseNpgsql(services.GetRequiredService<NpgsqlDataSource>())
            .AddInterceptors(refreshWriteFailure, logoutCommitFailure, customerRoleWriteFailure, adminInventoryWriteFailure, holdWriteFailure, firstHoldClaim, parallelHoldReads, overlapRead, conversionCommit));
        builder.Services.AddTicketBookingIdentity().AddSignInManager();
        builder.Services.AddTicketBookingAuthentication(builder.Configuration);
        builder.Services.AddApiErrors();
        builder.Services.AddCatalogueValidation();
        builder.Services.AddHoldValidation();
        builder.Services.AddBookingTiming(builder.Configuration);
        builder.Services.AddHoldExpiryProcessing(builder.Configuration);
        builder.Services.AddScoped<SessionSeatAllocationWriter>();
        builder.Services.AddScoped<HoldConversionService>();
        builder.Services.AddDocumentedOpenApi();
        app = builder.Build();
        app.UseExceptionHandler();
        app.UseStatusCodePages(ApiProblemDetails.WriteStatusCodeAsync);
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAuthEndpoints();
        app.MapAdminCatalogueEndpoints();
        app.MapCatalogueEndpoints();
        app.MapHoldEndpoints();
        app.MapOpenApi();
        app.MapGet("/test/protected", (ClaimsPrincipal user) => Results.Ok(new { subject = user.FindFirst("sub")!.Value }))
            .RequireAuthorization();
        app.MapGet("/test/admin", () => Results.NoContent()).RequireAuthorization(AuthorizationPolicies.Admin);
        app.MapGet("/test/customer", () => Results.NoContent()).RequireAuthorization(AuthorizationPolicies.Customer);
        app.MapPost("/test/holds/{holdId:guid}/convert", async (Guid holdId, HoldConversionService conversion, CancellationToken ct) =>
        {
            var booking = await conversion.ConvertAsync(holdId, Guid.NewGuid(), "BK-" + Guid.NewGuid().ToString("N"), ct);
            return booking is null ? Results.NotFound() : Results.Ok(new { booking.Id });
        }).RequireAuthorization(AuthorizationPolicies.Customer).ExcludeFromDescription();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().Database.MigrateAsync();
        }
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync()
    {
        client?.Dispose();
        if (app is not null)
        {
            await app.DisposeAsync();
        }
        await ExecuteSchemaCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE");
    }

    [PostgresFact]
    public async Task RegisterThenLogin_PersistsProfileAndReturnsAUsableJwtWithoutCredentials()
    {
        using var registered = await Register(" Alice@example.test ", " Alice ");
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var user = (await registered.Content.ReadFromJsonAsync<UserResponse>())!;
        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("Alice@example.test", user.Email);
        Assert.Equal("Alice", user.DisplayName);
        var body = await registered.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", body);
        Assert.DoesNotContain("accessToken", body);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var stored = (await manager.FindByIdAsync(user.Id.ToString()))!;
            Assert.Equal("Alice", stored.DisplayName);
            Assert.NotEqual(Password, stored.PasswordHash);
            Assert.True(await manager.CheckPasswordAsync(stored, Password));
        }

        using var login = await Login("alice@EXAMPLE.test", Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(login.Headers.CacheControl!.NoStore);
        var result = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.Equal(user, result.User);
        Assert.Equal("Bearer", result.TokenType);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);
        Assert.Equal("auth-tests", jwt.Issuer);
        Assert.Contains("auth-test-client", jwt.Audiences);
        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds()).AddMinutes(15), result.ExpiresAt);
        Assert.Equal(result.ExpiresAt.UtcDateTime, jwt.ValidTo);
        Assert.DoesNotContain(jwt.Claims, claim => claim.Type.Contains("password", StringComparison.OrdinalIgnoreCase));
        using var protectedResponse = await Protected(result.AccessToken);
        Assert.Equal(HttpStatusCode.OK, protectedResponse.StatusCode);
        Assert.Equal(user.Id.ToString(), (await protectedResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("subject").GetString());
    }

    [PostgresTheory]
    [InlineData(null, Password, "Alice", "email")]
    [InlineData("invalid-email", Password, "Alice", "email")]
    [InlineData("alice@example.test", null, "Alice", "password")]
    [InlineData("alice@example.test", "weak", "Alice", "password")]
    [InlineData("alice@example.test", Password, " ", "displayName")]
    public async Task RegistrationValidation_UsesStableErrorsAndDoesNotCreateAUser(string? email, string? password, string? displayName, string field)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password, displayName));

        var problem = await AssertProblem(response, 400, "validation_failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().Users.ToArrayAsync());
    }

    [PostgresFact]
    public async Task Registration_RejectsOverlongFields()
    {
        using var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            new string('a', 260) + "@example.test", new string('p', 129), new string('n', 201)));

        var problem = await AssertProblem(response, 400, "validation_failed");
        Assert.Equal(3, problem.GetProperty("errors").EnumerateObject().Count());
    }

    [PostgresFact]
    public async Task ConcurrentRegistrations_ProduceOneAccountAndSafeConflicts()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
            Register(index % 2 == 0 ? "Alice@example.test" : "ALICE@EXAMPLE.TEST")));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            foreach (var response in responses.Where(response => response.StatusCode != HttpStatusCode.Created))
            {
                await AssertProblem(response, 409, "account_already_exists");
            }
            await using var scope = app.Services.CreateAsyncScope();
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>().Users.CountAsync());
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
    public async Task DuplicateRegistration_PreservesTheOriginalAccount()
    {
        using var first = await Register();
        var original = (await first.Content.ReadFromJsonAsync<UserResponse>())!;
        using var duplicate = await Register("ALICE@EXAMPLE.TEST", "Replacement");
        await AssertProblem(duplicate, 409, "account_already_exists");
        using var login = await Login("alice@example.test", Password);
        Assert.Equal(original, (await login.Content.ReadFromJsonAsync<LoginResponse>())!.User);
    }

    [PostgresFact]
    public async Task UnknownAndIncorrectCredentials_ReturnTheSamePublicFailure()
    {
        using var registered = await Register();
        using var unknown = await Login("missing@example.test", Password);
        using var wrong = await Login("alice@example.test", "wrong-password");
        var missingProblem = await AssertProblem(unknown, 401, "invalid_credentials");
        var wrongProblem = await AssertProblem(wrong, 401, "invalid_credentials");
        Assert.Equal(missingProblem.GetProperty("detail").GetString(), wrongProblem.GetProperty("detail").GetString());
    }

    [PostgresFact]
    public async Task RepeatedFailedLogins_LockTheAccountAndRejectTheCorrectPassword()
    {
        using var registered = await Register();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failed = await Login("alice@example.test", "wrong-password");
            await AssertProblem(failed, 401, "invalid_credentials");
        }
        using var correct = await Login("alice@example.test", Password);
        await AssertProblem(correct, 401, "invalid_credentials");
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(await users.IsLockedOutAsync((await users.FindByEmailAsync("alice@example.test"))!));
    }

    [PostgresFact]
    public async Task TwoFactorAccount_CannotGetATokenUsingOnlyItsPassword()
    {
        using var registered = await Register();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.SetTwoFactorEnabledAsync((await users.FindByEmailAsync("alice@example.test"))!, true)).Succeeded);
        }
        using var login = await Login("alice@example.test", Password);
        await AssertProblem(login, 401, "invalid_credentials");
    }

    [PostgresFact]
    public async Task RoleClaims_ComeOnlyFromPersistedServerAssignments()
    {
        using var registered = await client.PostAsJsonAsync("/api/auth/register",
            new { email = "alice@example.test", password = Password, displayName = "Alice", roles = new[] { "Admin" } });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var initial = await LoginToken();
        using var forbidden = await Admin(initial.AccessToken);
        await AssertProblem(forbidden, 403, "forbidden");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            Assert.True(await roles.RoleExistsAsync("Admin"));
            Assert.True((await users.AddToRoleAsync((await users.FindByEmailAsync("alice@example.test"))!, "Admin")).Succeeded);
        }
        var assigned = await LoginToken();
        using var allowed = await Admin(assigned.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        Assert.NotEqual(initial.AccessToken, assigned.AccessToken);
    }

    [PostgresFact]
    public async Task RepeatedLogin_IssuesDistinctTokenIdsWithTheSameConfiguredLifetime()
    {
        using var registered = await Register();
        var first = await LoginToken();
        var second = await LoginToken();
        var handler = new JwtSecurityTokenHandler();

        Assert.NotEqual(handler.ReadJwtToken(first.AccessToken).Id, handler.ReadJwtToken(second.AccessToken).Id);
        Assert.Equal(first.ExpiresAt, second.ExpiresAt);
    }

    [PostgresTheory]
    [InlineData(null, Password)]
    [InlineData("invalid-email", Password)]
    [InlineData("alice@example.test", null)]
    [InlineData("alice@example.test", "")]
    public async Task LoginValidation_ReturnsBadRequestBeforeCredentialChecks(string? email, string? password)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        await AssertProblem(response, 400, "validation_failed");
    }

    [PostgresTheory]
    [InlineData(null)]
    [InlineData("malformed-token")]
    public async Task ProtectedRequest_RequiresAValidBearerToken(string? token)
    {
        using var response = token is null ? await client.GetAsync("/test/protected") : await Protected(token);
        await AssertProblem(response, 401, "unauthorized");
    }

    [PostgresFact]
    public async Task AccessToken_IsRejectedExactlyAtItsExpirationWithoutGrace()
    {
        using var registered = await Register();
        var login = await LoginToken();
        clock.Set(login.ExpiresAt.AddTicks(-1));
        using var before = await Protected(login.AccessToken);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        clock.Set(login.ExpiresAt);
        using var at = await Protected(login.AccessToken);
        await AssertProblem(at, 401, "unauthorized");
        clock.Set(login.ExpiresAt.AddTicks(1));
        using var after = await Protected(login.AccessToken);
        await AssertProblem(after, 401, "unauthorized");
    }

    [PostgresTheory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("future")]
    public async Task InvalidJwt_IsRejected(string invalidPart)
    {
        var now = clock.GetUtcNow();
        var token = new JwtSecurityToken(invalidPart == "issuer" ? "other-issuer" : "auth-tests",
            invalidPart == "audience" ? "other-audience" : "auth-test-client",
            [new Claim("sub", Guid.NewGuid().ToString())],
            now.AddMinutes(invalidPart == "future" ? 1 : -1).UtcDateTime, now.AddMinutes(15).UtcDateTime,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(invalidPart == "signature"
                ? "different-test-signing-key-at-least-32-bytes" : SigningKey)), SecurityAlgorithms.HmacSha256));
        using var response = await Protected(new JwtSecurityTokenHandler().WriteToken(token));
        await AssertProblem(response, 401, "unauthorized");
    }

    [PostgresTheory]
    [InlineData("/api/auth/register")]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/refresh")]
    [InlineData("/api/auth/logout")]
    public async Task MalformedJson_ReturnsTheStableBadRequestContract(string path)
    {
        using var response = await client.PostAsync(path, new StringContent("{broken", Encoding.UTF8, "application/json"));
        await AssertProblem(response, 400, "validation_failed");
    }

    [PostgresFact]
    public async Task UnsupportedAccept_StillReturnsSafeCredentialErrors()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("unknown@example.test", Password))
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using var response = await client.SendAsync(request);
        await AssertProblem(response, 401, "invalid_credentials");
    }

    private Task<HttpResponseMessage> Register(string email = "alice@example.test", string displayName = "Alice") =>
        client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password, displayName));

    private Task<HttpResponseMessage> Login(string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));

    private async Task<LoginResponse> LoginToken()
    {
        using var response = await Login("alice@example.test", Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private Task<HttpResponseMessage> Protected(string token) => AuthorizedRequest("/test/protected", token);
    private Task<HttpResponseMessage> Admin(string token) => AuthorizedRequest("/test/admin", token);

    private async Task<HttpResponseMessage> AuthorizedRequest(string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> AssertProblem(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Password, body);
        Assert.DoesNotContain(SigningKey, body);
        Assert.DoesNotContain("PasswordHash", body, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(body);
        var problem = json.RootElement;
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal(status, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("instance").GetString()));
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
        return problem.Clone();
    }

    private async Task ExecuteSchemaCommand(string sql)
    {
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class TestClock : TimeProvider
    {
        private long ticks = TimeProvider.System.GetUtcNow().UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void Set(DateTimeOffset value) => Interlocked.Exchange(ref ticks, value.UtcTicks);
    }
}
