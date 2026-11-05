using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Entity = TicketBooking.Domain.Reservation.SeatHoldItem;

namespace TicketBooking.Infrastructure.Persistence.Configurations;

public sealed class SeatHoldItemConfiguration : IEntityTypeConfiguration<Entity>
{
    public void Configure(EntityTypeBuilder<Entity> builder)
    {
        builder.ToTable("SeatHoldItems");
        builder.HasKey(item => new { item.SeatHoldId, item.SessionSeatId });
        builder.ComplexProperty(item => item.PriceSnapshot).ConfigureMoney("PriceSnapshot", "Currency");
        builder.HasOne<TicketBooking.Domain.Catalogue.SessionSeat>().WithMany()
            .HasForeignKey(item => item.SessionSeatId).OnDelete(DeleteBehavior.Restrict);
    }
}
