using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Domain.Reservation;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    [PostgresFact]
    public async Task HoldCache_CreationPopulatesAndOwnerReadHitsWithoutExtendingDeadline()
    {
        var setup = await OwnedHold();
        var key = $"hold:{setup.Hold.Id:D}";
        var entry = holdCacheStorage.Entries[key];
        Assert.Equal(setup.Hold.ExpiresAt, entry.Options.AbsoluteExpiration);
        var writes = holdCacheStorage.Writes;
        var reads = holdCacheStorage.Reads;
        clock.Set(clock.GetUtcNow().AddMinutes(2));

        using var read = await AdminRequest(HttpMethod.Get, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var returned = (await read.Content.ReadFromJsonAsync<HoldResponse>())!;
        Assert.Equal(setup.Hold.ExpiresAt, returned.ExpiresAt);
        Assert.Equal(setup.Hold.TotalAmount, returned.TotalAmount);
        Assert.Equal(setup.Hold.Items.ToArray(), returned.Items.ToArray());
        Assert.Equal(reads + 1, holdCacheStorage.Reads);
        Assert.Equal(writes, holdCacheStorage.Writes);

        // Even an Admin cannot consult another customer's populated hold cache.
        reads = holdCacheStorage.Reads;
        using var forbidden = await AdminRequest(HttpMethod.Get, $"/api/holds/{setup.Hold.Id}", setup.Admin);
        await AssertProblem(forbidden, 403, "resource_not_owned");
        Assert.Equal(reads, holdCacheStorage.Reads);
    }

    [PostgresFact]
    public async Task HoldCache_MissReloadsSnapshotsWithOnlyRemainingLifetime()
    {
        var setup = await OwnedHold();
        var key = $"hold:{setup.Hold.Id:D}";
        holdCacheStorage.Entries.Clear();
        clock.Set(setup.Hold.ExpiresAt.AddMinutes(-1));
        using var read = await AdminRequest(HttpMethod.Get, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var cached = holdCacheStorage.Entries[key];
        Assert.Equal(TimeSpan.FromMinutes(1), cached.Options.AbsoluteExpiration - clock.GetUtcNow());
        var payload = JsonSerializer.Deserialize<HoldResponse>(cached.Bytes)!;
        Assert.Equal(setup.Hold.Items.ToArray(), payload.Items.ToArray());
    }

    [PostgresTheory]
    [InlineData("cancel", SeatHoldStatus.Cancelled)]
    [InlineData("convert", SeatHoldStatus.Converted)]
    [InlineData("expire", SeatHoldStatus.Expired)]
    public async Task HoldCache_TerminalCommitInvalidatesAndStaleRefillsAreIgnored(string operation, SeatHoldStatus status)
    {
        var setup = await OwnedHold();
        var key = $"hold:{setup.Hold.Id:D}";
        var original = holdCacheStorage.Entries[key];
        if (operation == "expire")
        {
            clock.Set(setup.Hold.ExpiresAt);
            Assert.Equal(1, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        }
        else
        {
            using var result = operation == "cancel"
                ? await AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner)
                : await ConvertHold(setup.Hold.Id, setup.Owner);
            Assert.Equal(operation == "cancel" ? HttpStatusCode.NoContent : HttpStatusCode.OK, result.StatusCode);
        }
        Assert.False(holdCacheStorage.Entries.ContainsKey(key));

        // Simulate a slow reader filling Active data after terminal-state invalidation.
        holdCacheStorage.Entries[key] = original;
        var reads = holdCacheStorage.Reads;
        using var read = await AdminRequest(HttpMethod.Get, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal((int)status, (await read.Content.ReadFromJsonAsync<HoldResponse>())!.Status);
        Assert.Equal(reads, holdCacheStorage.Reads);
        Assert.False(holdCacheStorage.Entries.ContainsKey(key));
    }

    [PostgresFact]
    public async Task HoldCache_StaleDeadlineIsRejectedAndOriginalSnapshotsAreRestored()
    {
        var setup = await OwnedHold();
        var key = $"hold:{setup.Hold.Id:D}";
        var original = holdCacheStorage.Entries[key];
        holdCacheStorage.Entries[key] = original with
        {
            Bytes = JsonSerializer.SerializeToUtf8Bytes(setup.Hold with
            {
                ExpiresAt = setup.Hold.ExpiresAt.AddMinutes(5),
                TotalAmount = 999m,
                Items = setup.Hold.Items.Select(item => item with { Price = 999m }).ToArray()
            })
        };
        using var read = await AdminRequest(HttpMethod.Get, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var response = (await read.Content.ReadFromJsonAsync<HoldResponse>())!;
        Assert.Equal(setup.Hold.ExpiresAt, response.ExpiresAt);
        Assert.Equal(setup.Hold.TotalAmount, response.TotalAmount);
        Assert.Equal(setup.Hold.Items.ToArray(), response.Items.ToArray());
        Assert.Equal(setup.Hold.ExpiresAt, holdCacheStorage.Entries[key].Options.AbsoluteExpiration);
        var repaired = JsonSerializer.Deserialize<HoldResponse>(holdCacheStorage.Entries[key].Bytes)!;
        Assert.Equal(response.Items.ToArray(), repaired.Items.ToArray());
    }

    [PostgresTheory]
    [InlineData("cancel", SeatHoldStatus.Cancelled)]
    [InlineData("convert", SeatHoldStatus.Converted)]
    [InlineData("expire", SeatHoldStatus.Expired)]
    public async Task HoldCache_FailedInvalidationCannotExposeTheWarmActiveSnapshot(string operation, SeatHoldStatus status)
    {
        var setup = await OwnedHold();
        var key = $"hold:{setup.Hold.Id:D}";
        var original = holdCacheStorage.Entries[key];
        holdCacheStorage.FailRemovals = true;
        if (operation == "expire")
        {
            clock.Set(setup.Hold.ExpiresAt);
            Assert.Equal(1, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        }
        else
        {
            using var result = operation == "cancel"
                ? await AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner)
                : await ConvertHold(setup.Hold.Id, setup.Owner);
            Assert.Equal(operation == "cancel" ? HttpStatusCode.NoContent : HttpStatusCode.OK, result.StatusCode);
        }
        Assert.Same(original, holdCacheStorage.Entries[key]);
        var reads = holdCacheStorage.Reads;
        using var history = await AdminRequest(HttpMethod.Get, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Equal((int)status, (await history.Content.ReadFromJsonAsync<HoldResponse>())!.Status);
        Assert.Equal(reads, holdCacheStorage.Reads);
        Assert.Same(original, holdCacheStorage.Entries[key]);
        holdCacheStorage.FailRemovals = false;
        using var repaired = await AdminRequest(HttpMethod.Get, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        Assert.Equal(HttpStatusCode.OK, repaired.StatusCode);
        Assert.False(holdCacheStorage.Entries.ContainsKey(key));
    }

    [PostgresFact]
    public async Task HoldCache_RedisOutageDoesNotUndoCreationOrCancellation()
    {
        holdCacheStorage.FailReads = holdCacheStorage.FailWrites = holdCacheStorage.FailRemovals = true;
        var setup = await OwnedHold();
        using var read = await AdminRequest(HttpMethod.Get, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var cancelled = await AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        using var history = await AdminRequest(HttpMethod.Get, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        Assert.Equal((int)SeatHoldStatus.Cancelled, (await history.Content.ReadFromJsonAsync<HoldResponse>())!.Status);
    }

    [PostgresFact]
    public async Task HoldCache_FailedCancellationPreservesTheActiveSnapshot()
    {
        var setup = await OwnedHold();
        var key = $"hold:{setup.Hold.Id:D}";
        var original = holdCacheStorage.Entries[key];
        holdWriteFailure.FailCancellation = true;
        using var failed = await AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner);
        await AssertProblem(failed, 500, "internal_error");
        Assert.Same(original, holdCacheStorage.Entries[key]);
        await AssertOriginalHoldStillAllocated(setup.Hold);
    }

    [PostgresFact]
    public async Task HoldCache_FailedCreationNeverPopulatesCache()
    {
        var (token, session) = await HoldSession();
        holdWriteFailure.Enabled = true;
        using var failed = await AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", token,
            new CreateHoldRequest(session.Seats.Select(seat => seat.Id).ToArray()));
        await AssertProblem(failed, 500, "internal_error");
        Assert.Empty(holdCacheStorage.Entries);
        Assert.Equal(0, holdCacheStorage.Writes);
        await AssertNoHoldAllocation();
    }
}
