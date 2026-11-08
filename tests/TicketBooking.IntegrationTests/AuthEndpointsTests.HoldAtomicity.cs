using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    private readonly FirstHoldClaim firstHoldClaim = new();

    [PostgresFact]
    public async Task CustomerHold_ClaimsBecomeVisibleTogetherWithSnapshotsAndExpiration()
    {
        var (token, session) = await HoldSession();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var inventory = await db.SessionSeats.OrderBy(seat => seat.Id).ToArrayAsync();
        db.Entry(inventory[1]).ComplexProperty(seat => seat.Price).Property(price => price.Amount).CurrentValue = 12.50m;
        await db.SaveChangesAsync();
        var versions = inventory.ToDictionary(seat => seat.Id, seat => seat.Version);
        db.ChangeTracker.Clear();
        firstHoldClaim.Enabled = true;
        var pending = AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", token,
            new CreateHoldRequest(inventory.Select(seat => seat.Id).ToArray()));
        try
        {
            await firstHoldClaim.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            // Another connection sees the original committed inventory while the first claim is paused.
            Assert.All(await db.SessionSeats.AsNoTracking().ToArrayAsync(), seat =>
            {
                Assert.Equal(SessionSeatStatus.Available, seat.Status);
                Assert.Equal(versions[seat.Id], seat.Version);
            });
            Assert.Empty(await db.SeatHolds.ToArrayAsync());
            Assert.Empty(await db.SeatHoldItems.ToArrayAsync());
        }
        finally
        {
            firstHoldClaim.Continue.TrySetResult();
        }

        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<HoldResponse>())!;
        Assert.Equal(112.50m, result.TotalAmount);
        Assert.Equal("UAH", result.Currency);
        Assert.InRange(result.ExpiresAt, clock.GetUtcNow().AddMinutes(10).AddTicks(-9), clock.GetUtcNow().AddMinutes(10));
        var stored = await db.SeatHolds.Include(hold => hold.Items).SingleAsync();
        Assert.Equal(result.Id, stored.Id);
        Assert.Equal(result.ExpiresAt, stored.ExpiresAt);
        Assert.Equal(result.TotalAmount, stored.TotalAmount.Amount);
        Assert.Equal(2, stored.Items.Count);
        Assert.All(await db.SessionSeats.ToArrayAsync(), seat =>
        {
            Assert.Equal(SessionSeatStatus.Held, seat.Status);
            Assert.Equal(versions[seat.Id] + 1, seat.Version);
            var item = stored.Items.Single(value => value.SessionSeatId == seat.Id);
            Assert.Equal(seat.Version, item.AllocationVersion);
            Assert.Equal(seat.Price, item.PriceSnapshot);
        });

        // Later catalogue changes must not alter this hold's captured prices or total.
        var repriced = await db.SessionSeats.SingleAsync(seat => seat.Id == inventory[0].Id);
        db.Entry(repriced).ComplexProperty(seat => seat.Price).Property(price => price.Amount).CurrentValue = 999.99m;
        db.Entry(repriced).ComplexProperty(seat => seat.Price).Property(price => price.Currency).CurrentValue = "USD";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var historical = await db.SeatHolds.Include(hold => hold.Items).SingleAsync();
        Assert.Equal(result.ExpiresAt, historical.ExpiresAt);
        Assert.Equal(112.50m, historical.TotalAmount.Amount);
        Assert.Equal("UAH", historical.TotalAmount.Currency);
        foreach (var item in historical.Items)
        {
            var returned = result.Items.Single(value => value.SessionSeatId == item.SessionSeatId);
            Assert.Equal(returned.Price, item.PriceSnapshot.Amount);
            Assert.Equal(returned.Currency, item.PriceSnapshot.Currency);
        }
    }

    [PostgresFact]
    public async Task CustomerHold_ConflictOnLaterSeatRollsBackEarlierClaimAndPreservesWinner()
    {
        var (token, session) = await HoldSession();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var ids = await db.SessionSeats.OrderBy(seat => seat.Id).Select(seat => seat.Id).ToArrayAsync();
        firstHoldClaim.Enabled = true;
        var losingRequest = AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", token, new CreateHoldRequest(ids));
        HoldResponse winner;
        try
        {
            await firstHoldClaim.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            // The losing transaction has loaded both versions and updated the first seat.
            // This independent HTTP request wins the second seat before the loser reaches it.
            using var winningResponse = await AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", token,
                new CreateHoldRequest([ids[1]])).WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(HttpStatusCode.Created, winningResponse.StatusCode);
            winner = (await winningResponse.Content.ReadFromJsonAsync<HoldResponse>())!;
        }
        finally
        {
            firstHoldClaim.Continue.TrySetResult();
        }

        using var losingResponse = await losingRequest.WaitAsync(TimeSpan.FromSeconds(20));
        await AssertProblem(losingResponse, 409, "seat_not_available");
        var stored = await db.SeatHolds.Include(hold => hold.Items).SingleAsync();
        Assert.Equal(winner.Id, stored.Id);
        Assert.Equal(winner.ExpiresAt, stored.ExpiresAt);
        var item = Assert.Single(stored.Items);
        Assert.Equal(ids[1], item.SessionSeatId);
        Assert.Equal(1, item.AllocationVersion);
        var released = await db.SessionSeats.SingleAsync(seat => seat.Id == ids[0]);
        Assert.Equal(SessionSeatStatus.Available, released.Status);
        Assert.Equal(0, released.Version);
        var claimed = await db.SessionSeats.SingleAsync(seat => seat.Id == ids[1]);
        Assert.Equal(SessionSeatStatus.Held, claimed.Status);
        Assert.Equal(1, claimed.Version);
        Assert.Equal(1, await db.SeatHoldItems.CountAsync());
    }

    private sealed class FirstHoldClaim : DbCommandInterceptor
    {
        private int claims;
        private int holdTransitions;
        public bool Enabled { get; set; }
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource HoldTransitionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondHoldTransitionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("FROM \"SeatHolds\"", StringComparison.Ordinal) &&
                command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal) &&
                !command.CommandText.Contains("SKIP LOCKED", StringComparison.Ordinal))
            {
                HoldTransitionStarted.TrySetResult();
                if (Interlocked.Increment(ref holdTransitions) >= 2) SecondHoldTransitionStarted.TrySetResult();
            }
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (Enabled && result == 1 && command.CommandText.StartsWith("UPDATE \"SessionSeats\"", StringComparison.Ordinal) &&
                Interlocked.Increment(ref claims) == 1)
            {
                Reached.TrySetResult();
                await Continue.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}
