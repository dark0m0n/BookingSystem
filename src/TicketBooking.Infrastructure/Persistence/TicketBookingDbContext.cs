using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TicketBooking.Domain.Catalogue;
using TicketBooking.Domain.Reservation;
using TicketBooking.Domain.Venue;
using TicketBooking.Infrastructure.Identity;
using BookingEntity = TicketBooking.Domain.Booking.Booking;
using BookingItem = TicketBooking.Domain.Booking.BookingItem;
using PaymentEntity = TicketBooking.Domain.Payment.Payment;
using VenueEntity = TicketBooking.Domain.Venue.Venue;

namespace TicketBooking.Infrastructure.Persistence;

public sealed class TicketBookingDbContext(
    DbContextOptions<TicketBookingDbContext> options
) : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<VenueEntity> Venues => Set<VenueEntity>();
    public DbSet<Auditorium> Auditoriums => Set<Auditorium>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionSeat> SessionSeats => Set<SessionSeat>();
    public DbSet<SeatHold> SeatHolds => Set<SeatHold>();
    public DbSet<SeatHoldItem> SeatHoldItems => Set<SeatHoldItem>();
    public DbSet<BookingEntity> Bookings => Set<BookingEntity>();
    public DbSet<BookingItem> BookingItems => Set<BookingItem>();
    public DbSet<PaymentEntity> Payments => Set<PaymentEntity>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AdvanceSessionSeatVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AdvanceSessionSeatVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void AdvanceSessionSeatVersions()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<SessionSeat>().Where(entry => entry.State == EntityState.Modified))
        {
            var version = entry.Property(seat => seat.Version);
            // OriginalValue remains the WHERE token, including after a failed save.
            // Deriving from it also prevents retries from repeatedly advancing the candidate value.
            version.CurrentValue = checked(version.OriginalValue + 1);
            version.IsModified = true;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(TicketBookingDbContext).Assembly);
    }
}
