using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Administration;
using TicketBooking.Contracts.Auth;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Domain.Booking;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    private readonly OverlapInventoryRead overlapRead = new();
    private readonly ConversionCommit conversionCommit = new();

    [PostgresTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HoldRaces_OverlappingTwoSeatRequestsKeepOnlyWinnersWholeSelection(bool firstWins, bool reverseInput)
    {
        var admin = await AdminToken();
        var layout = await CreateAdminLayout(admin);
        await CreateAdmin<SeatResponse[]>($"/api/admin/auditoriums/{layout.Hall.Id}/seats", admin, new AddSeatsRequest([new("A", 3)]));
        var session = await CreateAdmin<SessionResponse>("/api/admin/sessions", admin, SessionRequest(layout));
        var firstUser = await OverlapCustomer("overlap-first@example.test");
        using var firstClient = firstUser.Client;
        var secondUser = await OverlapCustomer("overlap-second@example.test");
        using var secondClient = secondUser.Client;
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var ids = await db.SessionSeats.OrderBy(seat => seat.Id).Select(seat => seat.Id).ToArrayAsync();
        Assert.Equal(3, ids.Length);
        var firstSelection = new[] { ids[0], ids[1] };
        var secondSelection = new[] { ids[1], ids[2] };
        firstHoldClaim.Enabled = true;
        var firstRequest = firstClient.PostAsJsonAsync($"/api/sessions/{session.Id}/holds",
            new CreateHoldRequest(reverseInput ? firstSelection.Reverse().ToArray() : firstSelection));
        HttpResponseMessage? firstResponse = null;
        HttpResponseMessage? secondResponse = null;
        try
        {
            await firstHoldClaim.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            overlapRead.Enabled = true;
            var secondRequest = secondClient.PostAsJsonAsync($"/api/sessions/{session.Id}/holds",
                new CreateHoldRequest(reverseInput ? secondSelection.Reverse().ToArray() : secondSelection));
            await overlapRead.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            // First has provisionally claimed A; second has read B/C as available. Choose which
            // transaction reaches shared B first, without relying on timing or request input order.
            if (firstWins)
            {
                firstHoldClaim.Continue.TrySetResult();
                firstResponse = await firstRequest.WaitAsync(TimeSpan.FromSeconds(20));
                overlapRead.Continue.TrySetResult();
                secondResponse = await secondRequest.WaitAsync(TimeSpan.FromSeconds(20));
            }
            else
            {
                overlapRead.Continue.TrySetResult();
                secondResponse = await secondRequest.WaitAsync(TimeSpan.FromSeconds(20));
                firstHoldClaim.Continue.TrySetResult();
                firstResponse = await firstRequest.WaitAsync(TimeSpan.FromSeconds(20));
            }
            overlapRead.Enabled = false;
            var winner = firstWins ? firstResponse : secondResponse;
            var loser = firstWins ? secondResponse : firstResponse;
            Assert.Equal(HttpStatusCode.Created, winner.StatusCode);
            await AssertProblem(loser, 409, "seat_not_available");
            var created = (await winner.Content.ReadFromJsonAsync<HoldResponse>())!;
            var expected = firstWins ? firstSelection : secondSelection;
            var stored = await db.SeatHolds.Include(hold => hold.Items).SingleAsync();
            Assert.Equal(created.Id, stored.Id);
            Assert.Equal(firstWins ? firstUser.UserId : secondUser.UserId, stored.UserId);
            Assert.Equal(expected.Order(), stored.Items.Select(item => item.SessionSeatId).Order());
            Assert.Equal(2, await db.SeatHoldItems.CountAsync());
            Assert.Equal(200m, stored.TotalAmount.Amount);
            Assert.All(stored.Items, item => Assert.Equal(1, item.AllocationVersion));
            Assert.All(await db.SessionSeats.AsNoTracking().ToArrayAsync(), seat =>
            {
                Assert.Equal(expected.Contains(seat.Id) ? SessionSeatStatus.Held : SessionSeatStatus.Available, seat.Status);
                Assert.Equal(expected.Contains(seat.Id) ? 1 : 0, seat.Version);
            });
            // The losing request's exclusive seat is reusable, including A if its earlier claim rolled back.
            var exclusive = firstWins ? ids[2] : ids[0];
            var losingClient = firstWins ? secondClient : firstClient;
            using var retry = await losingClient.PostAsJsonAsync($"/api/sessions/{session.Id}/holds", new CreateHoldRequest([exclusive]));
            Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
            Assert.Equal(2, await db.SeatHolds.CountAsync());
            Assert.Equal(3, await db.SeatHoldItems.CountAsync());
            Assert.All(await db.SessionSeats.AsNoTracking().ToArrayAsync(), seat =>
            {
                Assert.Equal(SessionSeatStatus.Held, seat.Status);
                Assert.Equal(1, seat.Version);
            });
        }
        finally
        {
            overlapRead.Enabled = false;
            overlapRead.Continue.TrySetResult();
            firstHoldClaim.Continue.TrySetResult();
            firstResponse?.Dispose();
            secondResponse?.Dispose();
        }
    }

    [PostgresFact]
    public async Task HoldRaces_DueExpirySkipsValidatedConversionAwaitingCommitAndPreservesBooking()
    {
        var setup = await OwnedHold();
        clock.Set(setup.Hold.ExpiresAt.AddTicks(-1));
        conversionCommit.Enabled = true;
        var converting = ConvertHold(setup.Hold.Id, setup.Owner);
        try
        {
            // Conversion already passed its final time check while eligible. Delay the database
            // commit so expiry can observe the old committed Active state at the deadline.
            await conversionCommit.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            clock.Set(setup.Hold.ExpiresAt);
            var expiry = await ExpiryProcessor().ProcessBatchAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, expiry.Examined);
            Assert.Equal(0, expiry.Expired);
            await AssertOriginalHoldStillAllocated(setup.Hold);
            await using var before = app.Services.CreateAsyncScope();
            Assert.Empty(await before.ServiceProvider.GetRequiredService<TicketBookingDbContext>().Bookings.ToArrayAsync());
        }
        finally { conversionCommit.Continue.TrySetResult(); }
        using var response = await converting.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var afterExpiry = await ExpiryProcessor().ProcessBatchAsync();
        Assert.Equal(0, afterExpiry.Examined);
        Assert.Equal(0, afterExpiry.Expired);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        Assert.False(await scope.ServiceProvider.GetRequiredService<HoldExpiryReleaser>().ExpireAsync(setup.Hold.Id));
        var hold = await db.SeatHolds.Include(value => value.Items).SingleAsync();
        var booking = await db.Bookings.Include(value => value.Items).SingleAsync();
        Assert.Equal(SeatHoldStatus.Converted, hold.Status);
        Assert.Equal(BookingStatus.PendingPayment, booking.Status);
        Assert.True(booking.PaymentDeadline > clock.GetUtcNow());
        Assert.Equal(hold.TotalAmount, booking.TotalAmount);
        Assert.Equal(2, booking.Items.Count);
        Assert.All(hold.Items, item => Assert.Equal(1, item.AllocationVersion));
        Assert.All(booking.Items, item => Assert.Equal(2, item.AllocationVersion));
        Assert.All(await db.SessionSeats.ToArrayAsync(), seat =>
        {
            Assert.Equal(SessionSeatStatus.Held, seat.Status);
            Assert.Equal(2, seat.Version);
        });
    }

    private async Task<(HttpClient Client, Guid UserId)> OverlapCustomer(string email)
    {
        using var registered = await Register(email, "Overlap customer");
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        using var login = await Login(email, Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var credentials = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        var customer = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 1 })
        {
            BaseAddress = client.BaseAddress,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        customer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        return (customer, credentials.User.Id);
    }

    private sealed class OverlapInventoryRead : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context?.Database.CurrentTransaction is not null &&
                command.CommandText.Contains("FROM \"SessionSeats\"", StringComparison.Ordinal) &&
                !command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            {
                Reached.TrySetResult();
                await Continue.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class ConversionCommit : DbTransactionInterceptor
    {
        public bool Enabled { get; set; }
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context?.ChangeTracker.Entries<SeatHold>().Any(entry => entry.Entity.Status == SeatHoldStatus.Converted) == true)
            {
                Reached.TrySetResult();
                await Continue.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
