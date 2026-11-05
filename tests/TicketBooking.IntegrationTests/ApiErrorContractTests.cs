using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TicketBooking.Api.Errors;
using TicketBooking.Domain;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Persistence;
using BookingEntity = TicketBooking.Domain.Booking.Booking;
using PaymentEntity = TicketBooking.Domain.Payment.Payment;

namespace TicketBooking.IntegrationTests;

public class ApiErrorContractTests : IAsyncLifetime
{
    private WebApplication app = null!;
    private HttpClient client = null!;
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);
    private static readonly BookingTimingPolicy Timing = new(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1));

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production, Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddApiErrors();
        app = builder.Build();
        app.UseExceptionHandler();
        app.UseStatusCodePages(ApiProblemDetails.WriteStatusCodeAsync);
        app.MapGet("/seat-unavailable", () =>
        {
            CreateHold(SessionSeatStatus.Booked);
            return Results.NoContent();
        });
        app.MapGet("/hold-expired", () =>
        {
            var hold = CreateHold();
            hold.EnsureCanConvert(hold.UserId, new FixedClock(hold.ExpiresAt));
            return Results.NoContent();
        });
        app.MapGet("/not-owned", () =>
        {
            var hold = CreateHold();
            hold.EnsureCanConvert(Guid.NewGuid(), new FixedClock(Now));
            return Results.NoContent();
        });
        app.MapGet("/payment-processed", () =>
        {
            var payment = CreatePayment();
            payment.Succeed("provider-private");
            payment.Fail("provider-private");
            return Results.NoContent();
        });
        app.MapGet("/payment-replay", () =>
        {
            var payment = CreatePayment();
            payment.Succeed("provider-private");
            payment.Succeed("provider-private");
            return Results.NoContent();
        });
        app.MapGet("/seat-concurrency", () => Fail(new SeatAllocationConflictException()));
        app.MapGet("/other-concurrency", () => Fail(new DbUpdateConcurrencyException("private-provider-data")));
        app.MapGet("/bad-request", () => Fail(new BadHttpRequestException("private-parser-data", 400)));
        app.MapGet("/unexpected", () => Fail(new InvalidOperationException("private-password-and-sql")));
        app.MapGet("/unknown-domain-error", () => Fail(new DomainRuleException((DomainError)int.MaxValue, "private-future-error")));
        app.MapGet("/forbidden", () => Results.StatusCode(403));
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

    [Theory]
    [InlineData("/seat-unavailable", 409, "seat_not_available")]
    [InlineData("/hold-expired", 409, "hold_expired")]
    [InlineData("/payment-processed", 409, "payment_already_processed")]
    [InlineData("/not-owned", 403, "resource_not_owned")]
    [InlineData("/seat-concurrency", 409, "seat_not_available")]
    [InlineData("/other-concurrency", 409, "concurrency_conflict")]
    [InlineData("/bad-request", 400, "validation_failed")]
    [InlineData("/unexpected", 500, "internal_error")]
    [InlineData("/unknown-domain-error", 500, "internal_error")]
    [InlineData("/missing", 404, "not_found")]
    [InlineData("/forbidden", 403, "forbidden")]
    public async Task ErrorResponses_ExposeStableCodesAndSafeProblemDetails(string path, int status, string code)
    {
        using var response = await client.GetAsync(path);

        await AssertProblem(response, path, status, code);
    }

    [Fact]
    public async Task UnsupportedAccept_StillGetsTheApiErrorContract()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/unexpected");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using var response = await client.SendAsync(request);

        await AssertProblem(response, "/unexpected", 500, "internal_error");
    }

    [Fact]
    public async Task MethodNotAllowed_UsesTheSameProblemContract()
    {
        using var response = await client.PostAsync("/hold-expired", null);

        await AssertProblem(response, "/hold-expired", 405, "method_not_allowed");
    }

    [Fact]
    public async Task IdenticalPaymentOutcomeReplay_DoesNotProduceAnError()
    {
        using var response = await client.GetAsync("/payment-replay");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void EveryDeclaredDomainError_HasADistinctSafePublicMapping()
    {
        var definitions = Enum.GetValues<DomainError>()
            .Select(error => ApiErrorCatalog.ForException(new DomainRuleException(error, "private-diagnostic"))).ToArray();

        Assert.All(definitions, error =>
        {
            Assert.InRange(error.Status, 400, 499);
            Assert.Matches("^[a-z]+(?:_[a-z]+)*$", error.Code);
            Assert.DoesNotContain("private", error.Detail);
        });
        Assert.Equal(definitions.Length, definitions.Select(error => error.Code).Distinct().Count());
    }

    private static async Task AssertProblem(HttpResponseMessage response, string path, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement;
        Assert.Equal(status, problem.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal($"urn:ticketbooking:error:{code}", problem.GetProperty("type").GetString());
        Assert.Equal(path, problem.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
        Assert.DoesNotContain("private", body);
        Assert.DoesNotContain("stackTrace", body);
    }

    private static IResult Fail(Exception exception) => throw exception;

    private static SeatHold CreateHold(SessionSeatStatus status = SessionSeatStatus.Available)
    {
        var sessionId = Guid.NewGuid();
        return new SeatHold(Guid.NewGuid(), sessionId, Guid.NewGuid(), Now.AddHours(2),
            [new SessionSeat(Guid.NewGuid(), sessionId, Guid.NewGuid(), new Money(10m, "UAH"), status)], new FixedClock(Now), Timing);
    }

    private static PaymentEntity CreatePayment()
    {
        var hold = CreateHold();
        var booking = BookingEntity.CreateFromHold(Guid.NewGuid(), hold, hold.UserId, "BK-ERROR", new FixedClock(Now), Now.AddHours(2), Timing);
        return PaymentEntity.CreateForBooking(Guid.NewGuid(), booking, "request-key", new FixedClock(Now));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
