using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Entity = TicketBooking.Domain.Venue.Seat;

namespace TicketBooking.Infrastructure.Persistence.Configurations;

public sealed class SeatConfiguration : IEntityTypeConfiguration<Entity>
{
    public void Configure(EntityTypeBuilder<Entity> builder)
    {
        builder.ToTable("Seats");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(seat => seat.Row).IsRequired().UseCollation("C");
        builder.HasIndex(seat => new { seat.AuditoriumId, seat.Row, seat.Number }).IsUnique();
        builder.HasOne<TicketBooking.Domain.Venue.Auditorium>().WithMany()
            .HasForeignKey(seat => seat.AuditoriumId).OnDelete(DeleteBehavior.Restrict);
    }
}
