using System.Data.Common;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Domain.Reservation;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    private readonly CacheCommitFailure cacheCommitFailure = new();

    [PostgresFact]
    public async Task CacheCommits_FailedCreationCommitDoesNotPublishSavedButUncommittedHold()
    {
        var (token, session) = await HoldSession();
        cacheCommitFailure.FailNextCommit = true;
        using var failed = await AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", token,
            new CreateHoldRequest(session.Seats.Select(seat => seat.Id).ToArray()));
        await AssertProblem(failed, 500, "internal_error");
        Assert.False(cacheCommitFailure.FailNextCommit);
        Assert.Equal(0, holdCacheStorage.Writes);
        Assert.Equal(0, holdCacheStorage.Removals);
        Assert.Empty(holdCacheStorage.Entries);
        await AssertNoHoldAllocation();
    }

    [PostgresTheory]
    [InlineData("get-hit")]
    [InlineData("get-miss")]
    [InlineData("get-invalid")]
    [InlineData("get-terminal")]
    [InlineData("cancel")]
    [InlineData("replay")]
    [InlineData("convert")]
    [InlineData("expire")]
    public async Task CacheCommits_FailedHoldTransactionDoesNotPopulateOrInvalidate(string operation)
    {
        var setup = await OwnedHold();
        var key = $"hold:{setup.Hold.Id:D}";
        if (operation == "get-miss") holdCacheStorage.Entries.Clear();
        if (operation == "get-invalid")
        {
            var original = holdCacheStorage.Entries[key];
            holdCacheStorage.Entries[key] = original with
            {
                Bytes = JsonSerializer.SerializeToUtf8Bytes(setup.Hold with { Status = (int)SeatHoldStatus.Cancelled })
            };
        }
        if (operation is "replay" or "get-terminal")
        {
            var original = holdCacheStorage.Entries[key];
            using var cancelled = await AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner);
            Assert.Equal(System.Net.HttpStatusCode.NoContent, cancelled.StatusCode);
            // A stale entry must survive a failed commit; a later successful transaction repairs it.
            holdCacheStorage.Entries[key] = original;
        }
        if (operation == "expire") clock.Set(setup.Hold.ExpiresAt);

        var writes = holdCacheStorage.Writes;
        var removals = holdCacheStorage.Removals;
        var before = holdCacheStorage.Entries.ToArray();
        cacheCommitFailure.FailNextCommit = true;
        if (operation == "expire")
            Assert.Equal(0, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        else
        {
            using var failed = operation == "convert"
                ? await ConvertHold(setup.Hold.Id, setup.Owner)
                : await AdminRequest(operation is "cancel" or "replay" ? HttpMethod.Delete : HttpMethod.Get,
                    $"/api/holds/{setup.Hold.Id}", setup.Owner);
            await AssertProblem(failed, 500, "internal_error");
        }
        Assert.False(cacheCommitFailure.FailNextCommit);
        Assert.Equal(writes, holdCacheStorage.Writes);
        Assert.Equal(removals, holdCacheStorage.Removals);
        Assert.Equal(before.Length, holdCacheStorage.Entries.Count);
        foreach (var entry in before) Assert.Same(entry.Value, holdCacheStorage.Entries[entry.Key]);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var alreadyCancelled = operation is "replay" or "get-terminal";
        Assert.Equal(alreadyCancelled ? SeatHoldStatus.Cancelled : SeatHoldStatus.Active,
            (await db.SeatHolds.SingleAsync()).Status);
        Assert.Empty(await db.Bookings.ToArrayAsync());
        Assert.All(await db.SessionSeats.ToArrayAsync(), seat => Assert.Equal(alreadyCancelled ? 2 : 1, seat.Version));
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CacheCommits_FailedSeatMapReadCommitDoesNotFillOrInvalidate(bool hit)
    {
        var (_, session) = await HoldSession();
        EnableSmallSeatCache();
        if (hit) await SeatCacheMap(session.Id);
        var writes = holdCacheStorage.Writes;
        var removals = holdCacheStorage.Removals;
        cacheCommitFailure.FailNextCommit = true;
        using var failed = await client.GetAsync($"/api/sessions/{session.Id}/seats");
        await AssertProblem(failed, 500, "internal_error");
        Assert.False(cacheCommitFailure.FailNextCommit);
        Assert.Equal(writes, holdCacheStorage.Writes);
        Assert.Equal(removals, holdCacheStorage.Removals);
        Assert.Equal(hit ? 1 : 0, SeatMapEntries().Count());
    }

    private sealed class CacheCommitFailure : DbTransactionInterceptor
    {
        public bool FailNextCommit { get; set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (FailNextCommit)
            {
                FailNextCommit = false;
                throw new InvalidOperationException("Simulated database commit failure.");
            }
            return ValueTask.FromResult(result);
        }
    }
}
