using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Entity = TicketBooking.Domain.Payment.Payment;

namespace TicketBooking.Infrastructure.Persistence.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Entity>
{
    public void Configure(EntityTypeBuilder<Entity> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(payment => payment.IdempotencyKey).IsRequired().HasMaxLength(256).UseCollation("C");
        builder.Property(payment => payment.ProviderReference).HasMaxLength(256).UseCollation("C");
        builder.HasIndex(payment => new { payment.BookingId, payment.IdempotencyKey }).IsUnique();
        builder.HasIndex(payment => payment.ProviderReference).IsUnique().HasFilter("\"ProviderReference\" IS NOT NULL");
        builder.ComplexProperty(payment => payment.Amount).ConfigureMoney("Amount", "Currency");
        builder.HasOne<TicketBooking.Domain.Booking.Booking>().WithMany()
            .HasForeignKey(payment => payment.BookingId).OnDelete(DeleteBehavior.Restrict);
    }
}
