using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Entity = TicketBooking.Domain.Catalogue.SessionSeat;

namespace TicketBooking.Infrastructure.Persistence.Configurations;

public sealed class SessionSeatConfiguration : IEntityTypeConfiguration<Entity>
{
    public void Configure(EntityTypeBuilder<Entity> builder)
    {
        builder.ToTable("SessionSeats", table =>
            table.HasCheckConstraint("CK_SessionSeats_Version_NonNegative", "\"Version\" >= 0"));
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(seat => seat.Version).IsConcurrencyToken().ValueGeneratedNever();
        builder.ComplexProperty(seat => seat.Price).ConfigureMoney("Price", "Currency");
        builder.HasIndex(seat => new { seat.SessionId, seat.SeatId }).IsUnique();
        builder.HasOne<TicketBooking.Domain.Catalogue.Session>().WithMany()
            .HasForeignKey(seat => seat.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TicketBooking.Domain.Venue.Seat>().WithMany()
            .HasForeignKey(seat => seat.SeatId).OnDelete(DeleteBehavior.Restrict);
    }
}
