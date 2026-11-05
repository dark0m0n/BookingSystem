using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Entity = TicketBooking.Domain.Booking.Booking;

namespace TicketBooking.Infrastructure.Persistence.Configurations;

public sealed class BookingConfiguration : IEntityTypeConfiguration<Entity>
{
    public void Configure(EntityTypeBuilder<Entity> builder)
    {
        builder.ToTable("Bookings");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(booking => booking.Number).IsRequired().HasMaxLength(128).UseCollation("C");
        builder.HasIndex(booking => booking.Number).IsUnique();
        builder.ComplexProperty(booking => booking.TotalAmount).ConfigureMoney("TotalAmount", "Currency");
        builder.HasOne<TicketBooking.Domain.Catalogue.Session>().WithMany()
            .HasForeignKey(booking => booking.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(booking => booking.Items).WithOne()
            .HasForeignKey(item => item.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(booking => booking.Items).HasField("items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
