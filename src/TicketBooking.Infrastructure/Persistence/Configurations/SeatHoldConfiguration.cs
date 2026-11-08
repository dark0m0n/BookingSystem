using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Entity = TicketBooking.Domain.Reservation.SeatHold;

namespace TicketBooking.Infrastructure.Persistence.Configurations;

public sealed class SeatHoldConfiguration : IEntityTypeConfiguration<Entity>
{
    public void Configure(EntityTypeBuilder<Entity> builder)
    {
        builder.ToTable("SeatHolds");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(hold => hold.Status).IsConcurrencyToken();
        builder.HasIndex(hold => new { hold.ExpiresAt, hold.Id }).HasFilter("\"Status\" = 0");
        builder.ComplexProperty(hold => hold.TotalAmount).ConfigureMoney("TotalAmount", "Currency");
        builder.HasOne<TicketBooking.Domain.Catalogue.Session>().WithMany()
            .HasForeignKey(hold => hold.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(hold => hold.Items).WithOne()
            .HasForeignKey(item => item.SeatHoldId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(hold => hold.Items).HasField("items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
