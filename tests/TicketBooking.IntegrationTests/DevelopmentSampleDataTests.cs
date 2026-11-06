using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using TicketBooking.Api.Development;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Infrastructure.Persistence;
using PostgresFact = TicketBooking.IntegrationTests.PostgresPersistenceTests.PostgresFactAttribute;

namespace TicketBooking.IntegrationTests;

public class DevelopmentSampleDataTests : IAsyncLifetime
{
    private readonly string schema = "sample_seed_" + Guid.NewGuid().ToString("N");
    private readonly string? adminConnection = Environment.GetEnvironmentVariable("TICKETBOOKING_TEST_CONNECTION_STRING");
    private string testConnection = null!;
    private readonly SeedClock clock = new();

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(adminConnection)) return;
        await SchemaCommand($"CREATE SCHEMA \"{schema}\"");
        testConnection = new NpgsqlConnectionStringBuilder(adminConnection) { SearchPath = schema, Pooling = false }.ConnectionString;
        await using var db = PersistenceModelTests.CreateContext(testConnection);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => string.IsNullOrWhiteSpace(adminConnection) ? Task.CompletedTask : SchemaCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE");

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Command_RejectsOtherEnvironmentsBeforePersistence(string environment)
    {
        var builder = Builder(environment, "invalid-unused-connection");
        var count = builder.Services.Count;
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(1, await DevelopmentSampleDataCommand.RunAsync(builder, output, error));
        Assert.Equal(count, builder.Services.Count);
        Assert.Contains("only in Development", error.ToString());
        Assert.Empty(output.ToString());
    }

    [Fact]
    public async Task Command_RequiresConnectionBeforeBuildingTheHost()
    {
        var builder = Builder("Development", null);
        var count = builder.Services.Count;
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(1, await DevelopmentSampleDataCommand.RunAsync(builder, output, error));
        Assert.Equal(count, builder.Services.Count);
        Assert.Contains("ConnectionStrings:DefaultConnection", error.ToString());
    }

    [PostgresFact]
    public async Task Seed_CreatesPublishedMovieLayoutAndPricedFutureSessionAndRerunsUnchanged()
    {
        await using var db = PersistenceModelTests.CreateContext(testConnection);
        var seeder = Seeder(db);
        var first = await seeder.SeedAsync();
        Assert.True(first.CreatedSession);
        Assert.Equal(24, first.SeatCount);
        Assert.True(first.StartsAt > clock.GetUtcNow());
        Assert.Equal(TimeSpan.Zero, first.StartsAt.Offset);
        Assert.Equal(first.StartsAt, new DateTimeOffset(first.StartsAt.Ticks / TimeSpan.TicksPerMinute * TimeSpan.TicksPerMinute, TimeSpan.Zero));
        var session = await db.Sessions.AsNoTracking().SingleAsync();
        Assert.Equal(first.StartsAt.AddMinutes(120), session.EndsAt);
        Assert.Equal(EventStatus.Published, (await db.Events.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(first.VenueId, (await db.Auditoriums.AsNoTracking().SingleAsync()).VenueId);
        Assert.Equal(24, await db.Seats.CountAsync());
        Assert.Equal(3, await db.Seats.Select(s => s.Row).Distinct().CountAsync());
        var inventory = await db.SessionSeats.AsNoTracking().ToArrayAsync();
        Assert.All(inventory, s =>
        {
            Assert.Equal(first.SessionId, s.SessionId);
            Assert.Equal(150m, s.Price.Amount);
            Assert.Equal("UAH", s.Price.Currency);
            Assert.Equal(SessionSeatStatus.Available, s.Status);
            Assert.Equal(0, s.Version);
        });
        Assert.Equal((await db.Seats.Select(s => s.Id).ToArrayAsync()).Order(), inventory.Select(s => s.SeatId).Order());
        db.ChangeTracker.Clear();
        var repeated = await seeder.SeedAsync();
        Assert.False(repeated.CreatedSession);
        Assert.Equal(first.SessionId, repeated.SessionId);
        Assert.Equal(first.StartsAt, repeated.StartsAt);
        Assert.Equal(1, await db.Venues.CountAsync());
        Assert.Equal(1, await db.Auditoriums.CountAsync());
        Assert.Equal(1, await db.Events.CountAsync());
        Assert.Equal(1, await db.Sessions.CountAsync());
        Assert.Equal(24, await db.SessionSeats.CountAsync());
        Assert.Empty(await db.Users.ToArrayAsync());
    }

    [PostgresFact]
    public async Task Seed_PreservesClaimedInventoryAndCreatesAnotherFutureSessionAfterTimePasses()
    {
        await using var db = PersistenceModelTests.CreateContext(testConnection);
        var first = await Seeder(db).SeedAsync();
        var seat = await db.SessionSeats.AsNoTracking().FirstAsync();
        await new SessionSeatAllocationWriter(db).HoldAsync(seat.Id, 0);
        db.ChangeTracker.Clear();
        Assert.Equal(first.SessionId, (await Seeder(db).SeedAsync()).SessionId);
        clock.Now = first.StartsAt.AddHours(3);
        db.ChangeTracker.Clear();
        var next = await Seeder(db).SeedAsync();
        Assert.True(next.CreatedSession);
        Assert.NotEqual(first.SessionId, next.SessionId);
        Assert.True(next.StartsAt > clock.GetUtcNow());
        var oldSeat = await db.SessionSeats.AsNoTracking().SingleAsync(s => s.Id == seat.Id);
        Assert.Equal(SessionSeatStatus.Held, oldSeat.Status);
        Assert.Equal(1, oldSeat.Version);
        Assert.Equal(first.StartsAt, (await db.Sessions.AsNoTracking().SingleAsync(s => s.Id == first.SessionId)).StartsAt);
        Assert.Equal(24, await db.Seats.CountAsync());
        Assert.Equal(48, await db.SessionSeats.CountAsync());
    }

    [PostgresFact]
    public async Task Seed_ConcurrentRunsCreateOnlyOneSampleGraph()
    {
        await using var firstDb = PersistenceModelTests.CreateContext(testConnection);
        await using var secondDb = PersistenceModelTests.CreateContext(testConnection);
        var results = await Task.WhenAll(Seeder(firstDb).SeedAsync(), Seeder(secondDb).SeedAsync());
        Assert.Equal(results[0].SessionId, results[1].SessionId);
        Assert.Single(results, r => r.CreatedSession);
        Assert.Equal(1, await firstDb.Venues.CountAsync());
        Assert.Equal(1, await firstDb.Sessions.CountAsync());
        Assert.Equal(24, await firstDb.Seats.CountAsync());
        Assert.Equal(24, await firstDb.SessionSeats.CountAsync());
    }

    [PostgresFact]
    public async Task Seed_FailedInventorySaveRollsBackTheWholeGraphAndCanRetry()
    {
        var options = new DbContextOptionsBuilder<TicketBookingDbContext>().UseNpgsql(testConnection).AddInterceptors(new SeedFailure()).Options;
        await using (var failing = new TicketBookingDbContext(options))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Seeder(failing).SeedAsync());
        await using var db = PersistenceModelTests.CreateContext(testConnection);
        Assert.Empty(await db.Venues.ToArrayAsync());
        Assert.Empty(await db.Auditoriums.ToArrayAsync());
        Assert.Empty(await db.Events.ToArrayAsync());
        Assert.Empty(await db.Seats.ToArrayAsync());
        Assert.Empty(await db.Sessions.ToArrayAsync());
        Assert.Empty(await db.SessionSeats.ToArrayAsync());
        Assert.True((await Seeder(db).SeedAsync()).CreatedSession);
    }

    [PostgresFact]
    public async Task Seed_RefusesToRepairAnIncompleteUsedLayoutOrRepublishAnArchivedMovie()
    {
        await using var db = PersistenceModelTests.CreateContext(testConnection);
        var first = await Seeder(db).SeedAsync();
        var offering = await db.Events.SingleAsync();
        offering.UpdateDetails(offering.Title, offering.Description, offering.Duration, offering.AgeRating, EventStatus.Archived);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Seeder(db).SeedAsync());
        Assert.Equal(EventStatus.Archived, (await db.Events.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(24, await db.SessionSeats.CountAsync());
        Assert.Equal(first.StartsAt, (await db.Sessions.AsNoTracking().SingleAsync()).StartsAt);
        offering = await db.Events.SingleAsync();
        offering.UpdateDetails(offering.Title, offering.Description, offering.Duration, offering.AgeRating, EventStatus.Published);
        db.SessionSeats.Remove(await db.SessionSeats.FirstAsync());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Seeder(db).SeedAsync());
        Assert.Equal(24, await db.Seats.CountAsync());
        Assert.Equal(23, await db.SessionSeats.CountAsync());
    }

    [PostgresFact]
    public async Task CommandLine_SeedsAndExitsWithoutHttpOrJwtAndRejectsOtherEnvironments()
    {
        foreach (var environment in new[] { "Production", "Development" })
        {
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add(typeof(DevelopmentSampleDataCommand).Assembly.Location);
            start.ArgumentList.Add(DevelopmentSampleDataCommand.Switch);
            start.ArgumentList.Add("--environment"); start.ArgumentList.Add(environment);
            start.Environment["ConnectionStrings__DefaultConnection"] = testConnection;
            start.Environment["Logging__LogLevel__Default"] = "None";
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                Assert.Equal(environment == "Development" ? 0 : 1, process.ExitCode);
                Assert.DoesNotContain("Now listening", await output);
                Assert.DoesNotContain(testConnection, await error);
            }
            finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
        }
        await using var db = PersistenceModelTests.CreateContext(testConnection);
        Assert.Equal(1, await db.Sessions.CountAsync());
        Assert.Equal(24, await db.SessionSeats.CountAsync());
    }

    private DevelopmentSampleDataSeeder Seeder(TicketBookingDbContext db) => new(db, clock, Builder("Development", null).Environment);

    private static WebApplicationBuilder Builder(string environment, string? connection)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment, Args = [] });
        builder.Logging.ClearProviders(); builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connection });
        return builder;
    }

    private async Task SchemaCommand(string sql)
    {
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync(); await using var command = new NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync();
    }

    private sealed class SeedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2030, 1, 2, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class SeedFailure : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<SessionSeat>().Any()) throw new InvalidOperationException("Test-only seed write failure.");
            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }
    }
}
