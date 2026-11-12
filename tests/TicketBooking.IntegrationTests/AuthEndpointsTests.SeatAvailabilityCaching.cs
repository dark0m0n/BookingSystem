using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Contracts.Administration;
using TicketBooking.Contracts.Catalogue;
using TicketBooking.Contracts.Reservation;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Infrastructure.Caching;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;
using PostgresTheory = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresTheoryAttribute;

namespace TicketBooking.IntegrationTests;

public partial class AuthEndpointsTests
{
    private readonly SeatMapQueries seatMapQueries = new();

    [PostgresFact]
    public async Task SeatAvailabilityCache_LargeRepeatedMapAvoidsTheInventoryLayoutJoin()
    {
        var token = await AdminToken();
        var layout = await CreateAdminLayout(token);
        await CreateAdmin<SeatResponse[]>($"/api/admin/auditoriums/{layout.Hall.Id}/seats", token,
            new AddSeatsRequest(Enumerable.Range(3, 62).Select(number => new SeatRequest("A", number)).ToArray()));
        var session = await CreateAdmin<SessionResponse>("/api/admin/sessions", token, SessionRequest(layout));
        var first = await SeatCacheMap(session.Id);
        Assert.Equal(64, first.Seats.Count);
        var joined = seatMapQueries.Count;
        Assert.True(joined > 0);
        var cached = await SeatCacheMap(session.Id);
        Assert.Equal(first.Seats.ToArray(), cached.Seats.ToArray());
        Assert.Equal(joined, seatMapQueries.Count);
        Assert.Single(SeatMapEntries());
    }

    [PostgresFact]
    public async Task SeatAvailabilityCache_SmallAndDisabledMapsDoNotUseRedis()
    {
        var (_, session) = await HoldSession();
        await SeatCacheMap(session.Id);
        Assert.Equal(0, holdCacheStorage.Reads);
        Assert.Empty(SeatMapEntries());
        var options = app.Services.GetRequiredService<SeatAvailabilityCacheOptions>();
        options.MinimumSeatCount = 1;
        options.Enabled = false;
        await SeatCacheMap(session.Id);
        Assert.Equal(0, holdCacheStorage.Reads);
        Assert.Empty(SeatMapEntries());
    }

    [PostgresFact]
    public async Task SeatAvailabilityCache_HoldCreationChangesRevisionAndFailedClaimsKeepTheOldMap()
    {
        var (token, session) = await HoldSession();
        EnableSmallSeatCache();
        await SeatCacheMap(session.Id);
        var originalKey = Assert.Single(SeatMapEntries()).Key;
        holdWriteFailure.Enabled = true;
        using var failed = await AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", token,
            new CreateHoldRequest(session.Seats.Select(seat => seat.Id).ToArray()));
        await AssertProblem(failed, 500, "internal_error");
        holdWriteFailure.Enabled = false;
        var joined = seatMapQueries.Count;
        Assert.All((await SeatCacheMap(session.Id)).Seats, seat => Assert.Equal(0, seat.Status));
        Assert.Equal(joined, seatMapQueries.Count);
        Assert.Equal(originalKey, Assert.Single(SeatMapEntries()).Key);
        using var created = await AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", token,
            new CreateHoldRequest(session.Seats.Select(seat => seat.Id).ToArray()));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var next = await SeatCacheMap(session.Id);
        Assert.All(next.Seats, seat => { Assert.Equal(1, seat.Status); Assert.Equal(1, seat.Version); });
        Assert.Equal(2, SeatMapEntries().Count());
    }

    [PostgresTheory]
    [InlineData("cancel", SessionSeatStatus.Available, 2L)]
    [InlineData("expire", SessionSeatStatus.Available, 2L)]
    [InlineData("transfer", SessionSeatStatus.Held, 2L)]
    [InlineData("confirm", SessionSeatStatus.Booked, 3L)]
    [InlineData("release", SessionSeatStatus.Available, 2L)]
    [InlineData("release-booked", SessionSeatStatus.Available, 4L)]
    public async Task SeatAvailabilityCache_CommittedLifecycleWritesInvalidateThePriorRevision(
        string operation, SessionSeatStatus status, long version)
    {
        var setup = await OwnedHold();
        EnableSmallSeatCache();
        await SeatCacheMap(setup.Session.Id);
        if (operation == "cancel")
        {
            using var cancelled = await AdminRequest(HttpMethod.Delete, $"/api/holds/{setup.Hold.Id}", setup.Owner);
            Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        }
        else if (operation == "expire")
        {
            clock.Set(setup.Hold.ExpiresAt);
            Assert.Equal(1, (await ExpiryProcessor().ProcessBatchAsync()).Expired);
        }
        else
        {
            if (operation is "transfer" or "confirm" or "release-booked")
            {
                using var converted = await ConvertHold(setup.Hold.Id, setup.Owner);
                Assert.Equal(HttpStatusCode.Created, converted.StatusCode);
            }
            if (operation != "transfer")
            {
                await using var scope = app.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
                var writer = scope.ServiceProvider.GetRequiredService<SessionSeatAllocationWriter>();
                await using var transaction = await db.Database.BeginTransactionAsync();
                foreach (var seat in setup.Session.Seats.OrderBy(seat => seat.Id))
                {
                    if (operation == "release")
                        await writer.ReleaseAsync(seat.Id, 1, SessionSeatStatus.Held, SessionSeatStatus.Available);
                    else
                    {
                        var confirmed = await writer.ConfirmAsync(seat.Id, 2);
                        if (operation == "release-booked")
                            await writer.ReleaseAsync(seat.Id, confirmed, SessionSeatStatus.Booked, SessionSeatStatus.Available);
                    }
                }
                await transaction.CommitAsync();
            }
        }
        var next = await SeatCacheMap(setup.Session.Id);
        Assert.All(next.Seats, seat => { Assert.Equal((int)status, seat.Status); Assert.Equal(version, seat.Version); });
        Assert.Equal(2, SeatMapEntries().Count());
    }

    [PostgresFact]
    public async Task SeatAvailabilityCache_UncommittedWritesAndRollbackDoNotInvalidate()
    {
        var (_, session) = await HoldSession();
        EnableSmallSeatCache();
        await SeatCacheMap(session.Id);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<SessionSeatAllocationWriter>().HoldAsync(session.Seats[0].Id, 0);
        var joined = seatMapQueries.Count;
        Assert.All((await SeatCacheMap(session.Id)).Seats, seat => Assert.Equal(0, seat.Status));
        await transaction.RollbackAsync();
        Assert.All((await SeatCacheMap(session.Id)).Seats, seat => Assert.Equal(0, seat.Status));
        Assert.Equal(joined, seatMapQueries.Count);
        Assert.Single(SeatMapEntries());
    }

    [PostgresFact]
    public async Task SeatAvailabilityCache_LateOldFillCannotResurrectAvailabilityAfterAClaim()
    {
        var (token, session) = await HoldSession();
        EnableSmallSeatCache();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var filled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        holdCacheStorage.AfterSet = key =>
        {
            if (key.StartsWith("seat-availability:", StringComparison.Ordinal)) filled.TrySetResult();
        };
        holdCacheStorage.BeforeSetAsync = async key =>
        {
            if (!key.StartsWith("seat-availability:", StringComparison.Ordinal)) return;
            reached.TrySetResult();
            await resume.Task.WaitAsync(TimeSpan.FromSeconds(20));
        };
        var oldRead = SeatCacheMap(session.Id);
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            using var created = await AdminRequest(HttpMethod.Post, $"/api/sessions/{session.Id}/holds", token,
                new CreateHoldRequest(session.Seats.Select(seat => seat.Id).ToArray()));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }
        finally { resume.TrySetResult(); }
        await filled.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.All((await oldRead).Seats, seat => Assert.Equal(0, seat.Status));
        holdCacheStorage.BeforeSetAsync = null;
        Assert.All((await SeatCacheMap(session.Id)).Seats, seat => Assert.Equal(1, seat.Status));
        Assert.Equal(2, SeatMapEntries().Count());
    }

    [PostgresFact]
    public async Task SeatAvailabilityCache_TrackedRepricingInvalidatesAndRedisOutageFallsBack()
    {
        var (_, session) = await HoldSession();
        EnableSmallSeatCache();
        await SeatCacheMap(session.Id);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
            var seat = await db.SessionSeats.SingleAsync(seat => seat.Id == session.Seats[0].Id);
            db.Entry(seat).ComplexProperty(seat => seat.Price).Property(price => price.Amount).CurrentValue = 777m;
            await db.SaveChangesAsync();
        }
        holdCacheStorage.FailReads = holdCacheStorage.FailWrites = true;
        var current = (await SeatCacheMap(session.Id)).Seats.Single(seat => seat.Id == session.Seats[0].Id);
        Assert.Equal(777m, current.Price);
        Assert.Equal(1, current.Version);
    }

    [PostgresFact]
    public async Task SeatAvailabilityCache_CachedMapDoesNotBypassVisibilityOrLayoutValidation()
    {
        var (_, session) = await HoldSession();
        EnableSmallSeatCache();
        await SeatCacheMap(session.Id);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketBookingDbContext>();
        var inventory = await db.SessionSeats.SingleAsync(seat => seat.Id == session.Seats[0].Id);
        db.SessionSeats.Remove(inventory);
        await db.SaveChangesAsync();
        using var broken = await client.GetAsync($"/api/sessions/{session.Id}/seats");
        await AssertProblem(broken, 409, "conflict");
        var storedSession = await db.Sessions.SingleAsync();
        db.Entry(storedSession).Property(value => value.Status).CurrentValue = SessionStatus.Cancelled;
        await db.SaveChangesAsync();
        var reads = holdCacheStorage.Reads;
        using var hidden = await client.GetAsync($"/api/sessions/{session.Id}/seats");
        await AssertProblem(hidden, 404, "not_found");
        Assert.Equal(reads, holdCacheStorage.Reads);
    }

    private void EnableSmallSeatCache() => app.Services.GetRequiredService<SeatAvailabilityCacheOptions>().MinimumSeatCount = 1;

    private IEnumerable<KeyValuePair<string, RecordingDistributedCache.Entry>> SeatMapEntries() =>
        holdCacheStorage.Entries.Where(entry => entry.Key.StartsWith("seat-availability:", StringComparison.Ordinal));

    private async Task<SessionSeatMapResponse> SeatCacheMap(Guid sessionId)
    {
        using var read = await client.GetAsync($"/api/sessions/{sessionId}/seats");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.True(read.Headers.CacheControl?.NoStore);
        return (await read.Content.ReadFromJsonAsync<SessionSeatMapResponse>())!;
    }

    private sealed class SeatMapQueries : DbCommandInterceptor
    {
        private int count;
        public int Count => Volatile.Read(ref count);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("JOIN \"Seats\"", StringComparison.Ordinal) &&
                command.CommandText.Contains("FROM \"SessionSeats\"", StringComparison.Ordinal))
                Interlocked.Increment(ref count);
            return ValueTask.FromResult(result);
        }
    }
}
