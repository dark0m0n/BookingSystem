using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using TicketBooking.Api.Auth;
using TicketBooking.Api.Errors;
using TicketBooking.Application.Identity;
using TicketBooking.Infrastructure.Identity;

namespace TicketBooking.IntegrationTests;

public class JwtBearerValidationTests : IAsyncLifetime
{
    private const string Issuer = "jwt-validation-tests";
    private const string Audience = "jwt-validation-client";
    private const string SigningKey = "test-only-jwt-validation-signing-key-with-more-than-sixty-four-bytes";
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid ResourceOwnerId = Guid.NewGuid();
    private readonly TestClock clock = new();
    private WebApplication app = null!;
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production, Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Audience,
            ["Jwt:SigningKey"] = SigningKey,
            ["Jwt:AccessTokenMinutes"] = "15"
        });
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddTicketBookingAuthentication(builder.Configuration);
        builder.Services.AddApiErrors();
        app = builder.Build();
        app.UseExceptionHandler();
        app.UseStatusCodePages(ApiProblemDetails.WriteStatusCodeAsync);
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/test/protected", (ClaimsPrincipal user) => Results.Ok(new
        {
            subject = user.FindFirst("sub")?.Value,
            name = user.Identity?.Name,
            customer = user.IsInRole("Customer")
        })).RequireAuthorization();
        app.MapGet("/test/owned", (ResourceOwnershipService ownership) =>
        {
            ownership.EnsureOwner(ResourceOwnerId);
            return Results.Ok();
        }).RequireAuthorization(AuthorizationPolicies.Customer);
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
    }

    [Fact]
    public async Task OwnershipRoute_AnonymousCallerIsChallenged()
    {
        using var response = await client.GetAsync("/test/owned");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    [Theory]
    [InlineData(true, "Customer", HttpStatusCode.OK)]
    [InlineData(true, "Admin", HttpStatusCode.OK)]
    [InlineData(false, "Customer", HttpStatusCode.Forbidden)]
    [InlineData(false, "Admin", HttpStatusCode.Forbidden)]
    public async Task OwnershipRoute_RequiresOwnerEvenForAnAdmin(bool isOwner, string role, HttpStatusCode expected)
    {
        var user = new ApplicationUser { Id = isOwner ? ResourceOwnerId : Guid.NewGuid(), Email = "caller@example.test" };
        await using var scope = app.Services.CreateAsyncScope();
        var token = scope.ServiceProvider.GetRequiredService<JwtTokenIssuer>().Issue(user, [role]);

        using var response = await Send(token.AccessToken, path: "/test/owned?userId=" + ResourceOwnerId);

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.Forbidden)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("resource_not_owned", body.GetProperty("code").GetString());
            Assert.DoesNotContain(ResourceOwnerId.ToString(), body.ToString());
        }
    }

    [Theory]
    [InlineData("missing-subject")]
    [InlineData("invalid-subject")]
    [InlineData("empty-subject")]
    public async Task OwnershipRoute_SignedTokenWithoutAUsableSubjectRequiresAuthentication(string defect)
    {
        using var response = await Send(CreateToken(defect), path: "/test/owned");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unauthorized", body.GetProperty("code").GetString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ProductionTokenIssuer_ProducesAnAcceptedTokenWithExpectedClaims()
    {
        var user = new ApplicationUser { Email = "customer@example.test", DisplayName = "Customer" };
        await using var scope = app.Services.CreateAsyncScope();
        var token = scope.ServiceProvider.GetRequiredService<JwtTokenIssuer>().Issue(user, ["Customer"]);

        using var response = await Send(token.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(user.Id.ToString(), body.GetProperty("subject").GetString());
        Assert.Equal(user.Id.ToString(), body.GetProperty("name").GetString());
        Assert.True(body.GetProperty("customer").GetBoolean());
    }

    [Theory]
    [InlineData("missing-issuer")]
    [InlineData("wrong-issuer")]
    [InlineData("issuer-case")]
    [InlineData("missing-audience")]
    [InlineData("wrong-audience")]
    [InlineData("audience-case")]
    [InlineData("audience-trailing-slash")]
    [InlineData("missing-expiration")]
    [InlineData("expired")]
    [InlineData("not-yet-valid")]
    [InlineData("unsigned")]
    [InlineData("wrong-key")]
    [InlineData("wrong-algorithm")]
    [InlineData("tampered-payload")]
    public async Task InvalidToken_IsRejectedByTheProductionBearerPipeline(string defect)
    {
        using var response = await Send(CreateToken(defect));

        await AssertUnauthorized(response);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task Expiration_RejectsAtTheExactCutoffWithoutClockSkew(long ticksFromExpiration, bool accepted)
    {
        var expiration = Now.AddMinutes(15);
        clock.Set(expiration.AddTicks(ticksFromExpiration));

        using var response = await Send(CreateToken());

        if (accepted)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        else
        {
            await AssertUnauthorized(response);
        }
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task NotBefore_AcceptsOnlyAtOrAfterTheExactStart(long ticksFromStart, bool accepted)
    {
        var notBefore = Now.AddMinutes(1);
        clock.Set(notBefore.AddTicks(ticksFromStart));

        using var response = await Send(CreateToken("not-yet-valid"));

        if (accepted)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        else
        {
            await AssertUnauthorized(response);
        }
    }

    [Fact]
    public async Task InvalidToken_WithUnsupportedAcceptStillReturnsSafeProblemDetails()
    {
        using var response = await Send(CreateToken("unsigned"), "text/html");

        await AssertUnauthorized(response);
    }

    private static string CreateToken(string? defect = null)
    {
        var issuer = defect switch
        {
            "missing-issuer" => null,
            "wrong-issuer" => "other-issuer",
            "issuer-case" => Issuer.ToUpperInvariant(),
            _ => Issuer
        };
        var audience = defect switch
        {
            "missing-audience" => null,
            "wrong-audience" => "other-audience",
            "audience-case" => Audience.ToUpperInvariant(),
            "audience-trailing-slash" => Audience + "/",
            _ => Audience
        };
        var key = defect == "wrong-key" ? "different-test-only-signing-key-with-more-than-sixty-four-bytes-total" : SigningKey;
        var credentials = defect == "unsigned" ? null : new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            defect == "wrong-algorithm" ? SecurityAlgorithms.HmacSha384 : SecurityAlgorithms.HmacSha256);
        DateTime? expires = defect switch
        {
            "missing-expiration" => null,
            "expired" => Now.AddSeconds(-1).UtcDateTime,
            _ => Now.AddMinutes(15).UtcDateTime
        };
        var claims = new List<Claim>();
        if (defect != "missing-subject")
        {
            claims.Add(new Claim("sub", defect switch
            {
                "invalid-subject" => "invalid-user",
                "empty-subject" => Guid.Empty.ToString(),
                _ => Guid.NewGuid().ToString()
            }));
        }
        claims.Add(new Claim("role", "Customer"));
        var token = new JwtSecurityToken(issuer, audience, claims,
            Now.AddMinutes(defect == "not-yet-valid" ? 1 : -1).UtcDateTime, expires, credentials);
        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        if (defect == "tampered-payload")
        {
            var parts = encoded.Split('.');
            var payload = JsonNode.Parse(Base64UrlEncoder.Decode(parts[1]))!;
            payload["sub"] = "attacker";
            payload["role"] = "Admin";
            parts[1] = Base64UrlEncoder.Encode(payload.ToJsonString());
            encoded = string.Join('.', parts);
        }
        return encoded;
    }

    private async Task<HttpResponseMessage> Send(string token, string? accept = null, string path = "/test/protected")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (accept is not null)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        }
        return await client.SendAsync(request);
    }

    private static async Task AssertUnauthorized(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.True(string.IsNullOrEmpty(challenge.Parameter));
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(SigningKey, body);
        Assert.DoesNotContain("IDX", body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("unauthorized", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(401, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("/test/protected", json.RootElement.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("traceId").GetString()));
    }

    private sealed class TestClock : TimeProvider
    {
        private long ticks = Now.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void Set(DateTimeOffset value) => Interlocked.Exchange(ref ticks, value.UtcTicks);
    }
}
