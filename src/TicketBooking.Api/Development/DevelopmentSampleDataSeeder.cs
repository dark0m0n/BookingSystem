using Microsoft.EntityFrameworkCore;
using TicketBooking.Domain;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Venue;
using TicketBooking.Infrastructure.Persistence;
using VenueEntity = TicketBooking.Domain.Venue.Venue;

namespace TicketBooking.Api.Development;

public sealed class DevelopmentSampleDataSeeder(TicketBookingDbContext db, TimeProvider clock, IHostEnvironment environment)
{
    private const long AdvisoryLockKey = 687293451027L;
    public static readonly Guid VenueId = new("8d6c73e5-447b-4480-83de-000000000001");
    public static readonly Guid AuditoriumId = new("8d6c73e5-447b-4480-83de-000000000002");
    public static readonly Guid EventId = new("8d6c73e5-447b-4480-83de-000000000003");

    public async Task<DevelopmentSampleDataResult> SeedAsync(CancellationToken ct = default)
    {
        if (!environment.IsDevelopment()) throw new InvalidOperationException("Sample data is available only in Development.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize reruns, including two commands starting against an empty database.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey})", ct);
        if (!await db.Venues.AnyAsync(v => v.Id == VenueId, ct))
            db.Venues.Add(new VenueEntity(VenueId, "Sample Cinema", "1 Sample Street, Kyiv"));
        if (!await db.Auditoriums.AnyAsync(a => a.Id == AuditoriumId, ct))
            db.Auditoriums.Add(new Auditorium(AuditoriumId, VenueId, "Sample Auditorium"));
        if (!await db.Events.AnyAsync(e => e.Id == EventId, ct))
            db.Events.Add(new Event(EventId, "Sample Movie: Beyond the Horizon", "A fictional movie for local development.",
                TimeSpan.FromMinutes(120), 12, EventStatus.Published));
        await db.SaveChangesAsync(ct);

        // Use the same auditorium/event lock order as Admin session creation.
        var auditorium = await db.Auditoriums.FromSqlInterpolated($"SELECT * FROM \"Auditoriums\" WHERE \"Id\" = {AuditoriumId} FOR UPDATE")
            .AsNoTracking().SingleAsync(ct);
        var offering = await db.Events.FromSqlInterpolated($"SELECT * FROM \"Events\" WHERE \"Id\" = {EventId} FOR SHARE")
            .AsNoTracking().SingleAsync(ct);
        if (auditorium.VenueId != VenueId || offering.Status != EventStatus.Published)
            throw new InvalidOperationException("Sample catalogue relationships or publication were changed.");
        var seats = await db.Seats.AsNoTracking().Where(s => s.AuditoriumId == AuditoriumId).ToListAsync(ct);
        if (seats.Count == 0)
        {
            if (await db.Sessions.AnyAsync(s => s.AuditoriumId == AuditoriumId, ct))
                throw new InvalidOperationException("A used sample auditorium cannot have its layout rebuilt.");
            foreach (var row in new[] { "A", "B", "C" })
            {
                for (var number = 1; number <= 8; number++)
                {
                    var type = row == "B" ? SeatType.Premium : row == "C" && number <= 2 ? SeatType.Accessible : SeatType.Standard;
                    seats.Add(new Seat(Guid.NewGuid(), AuditoriumId, row, number, type));
                }
            }
            db.Seats.AddRange(seats);
        }

        var now = clock.GetUtcNow().ToUniversalTime();
        var existing = await db.Sessions.AsNoTracking()
            .Where(s => s.EventId == EventId && s.AuditoriumId == AuditoriumId && s.Status == SessionStatus.Scheduled && s.StartsAt > now)
            .OrderBy(s => s.StartsAt).ThenBy(s => s.Id).FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            var physicalIds = seats.Select(s => s.Id).ToHashSet();
            var inventoryIds = await db.SessionSeats.Where(s => s.SessionId == existing.Id).Select(s => s.SeatId).ToListAsync(ct);
            if (!physicalIds.SetEquals(inventoryIds)) throw new InvalidOperationException("Sample session inventory is incomplete.");
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(VenueId, AuditoriumId, EventId, existing.Id, seats.Count, existing.StartsAt, false);
        }

        var tomorrow = now.AddDays(1);
        var start = new DateTimeOffset(tomorrow.Ticks - tomorrow.Ticks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
        var session = new Session(Guid.NewGuid(), EventId, AuditoriumId, start, start.Add(offering.Duration));
        db.Sessions.Add(session);
        db.SessionSeats.AddRange(seats.Select(s => new SessionSeat(Guid.NewGuid(), session.Id, s.Id, new Money(150m, "UAH"))));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(VenueId, AuditoriumId, EventId, session.Id, seats.Count, start, true);
    }
}
