using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Entity = TicketBooking.Domain.Booking.BookingItem;

namespace TicketBooking.Infrastructure.Persistence.Configurations;

public sealed class BookingItemConfiguration : IEntityTypeConfiguration<Entity>
{
    public void Configure(EntityTypeBuilder<Entity> builder)
    {
        builder.ToTable("BookingItems", table => table.HasCheckConstraint(
            "CK_BookingItems_AllocationVersion_Positive", "\"AllocationVersion\" IS NULL OR \"AllocationVersion\" > 0"));
        builder.HasKey(item => new { item.BookingId, item.SessionSeatId });
        builder.ComplexProperty(item => item.PriceSnapshot).ConfigureMoney("PriceSnapshot", "Currency");
        builder.HasOne<TicketBooking.Domain.Catalogue.SessionSeat>().WithMany()
            .HasForeignKey(item => item.SessionSeatId).OnDelete(DeleteBehavior.Restrict);
    }
}
