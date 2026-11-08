using System.Collections.Concurrent;
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TicketBooking.Contracts.Auth;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    private const int ParallelHoldCount = 20;
    private readonly ParallelHoldReadBarrier parallelHoldReads = new();

    [PostgresFact]
    public async Task ParallelHolds_TwentyCustomersOnDistinctConnectionsProduceOneSuccessAndNineteenConflicts()
    {
        var (_, session) = await HoldSession();
        var selected = session.Seats[0];
        // Each handler owns its connection pool; HTTP/1.1 also prevents shared multiplexed streams.
        var clients = Enumerable.Range(0, ParallelHoldCount).Select(_ => new HttpClient(new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 1
        })
        {
            BaseAddress = client.BaseAddress,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        }).ToArray();
        HttpResponseMessage[]? responses = null;
        try
        {
            var users = await Task.WhenAll(clients.Select(async (customer, index) =>
            {
                var email = $"seat-racer-{index}@example.test";
                using var registered = await customer.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password, $"Racer {index}"));
                Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
                using var login = await customer.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
                Assert.Equal(HttpStatusCode.OK, login.StatusCode);
                var credentials = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
                Assert.Equal(credentials.User.Id.ToString(), new JwtSecurityTokenHandler().ReadJwtToken(credentials.AccessToken).Subject);
                customer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
                return credentials.User.Id;
            }));
            Assert.Equal(ParallelHoldCount, users.Distinct().Count());
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            parallelHoldReads.Enabled = true;
            var pending = clients.Select(async customer =>
            {
                await start.Task;
                return await customer.PostAsJsonAsync($"/api/sessions/{session.Id}/holds", new CreateHoldRequest([selected.Id]));
            }).ToArray();
            start.TrySetResult();
            try
            {
                await parallelHoldReads.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.Equal(ParallelHoldCount, parallelHoldReads.Connections.Count);
                Assert.Equal(ParallelHoldCount, parallelHoldReads.Connections.Values.Select(value => value.BackendPid).Distinct().Count());
                Assert.Equal(ParallelHoldCount, parallelHoldReads.Connections.Values.Select(value => value.TransactionId).Distinct().Count());
            }
            finally
            {
                parallelHoldReads.Continue.TrySetResult();
            }
            responses = await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(30));
            parallelHoldReads.Enabled = false;
            Assert.Equal(ParallelHoldCount, responses.Length);
            var winner = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            var conflicts = responses.Where(response => response.StatusCode == HttpStatusCode.Conflict).ToArray();
            Assert.Equal(ParallelHoldCount - 1, conflicts.Length);
            foreach (var conflict in conflicts) await AssertProblem(conflict, 409, "seat_not_available");
            var created = (await winner.Content.ReadFromJsonAsync<HoldResponse>())!;
            var winnerIndex = Array.IndexOf(responses, winner);
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var hold = await db.SeatHolds.Include(value => value.Items).SingleAsync();
            Assert.Equal(created.Id, hold.Id);
            Assert.Equal(users[winnerIndex], hold.UserId);
            Assert.Equal(SeatHoldStatus.Active, hold.Status);
            Assert.Equal(session.Id, hold.SessionId);
            Assert.Equal(created.ExpiresAt, hold.ExpiresAt);
            Assert.Equal(selected.Price, hold.TotalAmount.Amount);
            Assert.Equal(selected.Currency, hold.TotalAmount.Currency);
            var item = Assert.Single(hold.Items);
            Assert.Equal(selected.Id, item.SessionSeatId);
            Assert.Equal(1, item.AllocationVersion);
            Assert.Equal(selected.Price, item.PriceSnapshot.Amount);
            Assert.Equal(selected.Currency, item.PriceSnapshot.Currency);
            Assert.Equal(1, await db.SeatHoldItems.CountAsync());
            var seat = await db.SessionSeats.SingleAsync(value => value.Id == selected.Id);
            Assert.Equal(SessionSeatStatus.Held, seat.Status);
            Assert.Equal(1, seat.Version);
            Assert.All(await db.SessionSeats.Where(value => value.Id != selected.Id).ToArrayAsync(), other =>
            {
                Assert.Equal(SessionSeatStatus.Available, other.Status);
                Assert.Equal(0, other.Version);
            });
        }
        finally
        {
            parallelHoldReads.Enabled = false;
            parallelHoldReads.Continue.TrySetResult();
            if (responses is not null) foreach (var response in responses) response.Dispose();
            foreach (var customer in clients) customer.Dispose();
        }
    }

    private sealed class ParallelHoldReadBarrier : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public ConcurrentDictionary<Guid, (int BackendPid, Guid TransactionId)> Connections { get; } = new();
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context?.Database.CurrentTransaction is { } transaction &&
                command.CommandText.Contains("FROM \"SessionSeats\"", StringComparison.Ordinal) &&
                !command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            {
                var connection = (NpgsqlConnection)command.Connection!;
                Connections.TryAdd(eventData.Context!.ContextId.InstanceId, (connection.ProcessID, transaction.TransactionId));
                if (Connections.Count == ParallelHoldCount) Ready.TrySetResult();
                await Continue.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
