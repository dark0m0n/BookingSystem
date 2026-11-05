using Microsoft.EntityFrameworkCore;

namespace TicketBooking.Infrastructure.Persistence;

public sealed class TicketBookingDbContext(
    DbContextOptions<TicketBookingDbContext> options
) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(TicketBookingDbContext).Assembly);
    }
}
