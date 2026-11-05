using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Entity = TicketBooking.Domain.Venue.Auditorium;

namespace TicketBooking.Infrastructure.Persistence.Configurations;

public sealed class AuditoriumConfiguration : IEntityTypeConfiguration<Entity>
{
    public void Configure(EntityTypeBuilder<Entity> builder)
    {
        builder.ToTable("Auditoriums");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(auditorium => auditorium.Name).IsRequired();
        builder.HasOne<TicketBooking.Domain.Venue.Venue>().WithMany()
            .HasForeignKey(auditorium => auditorium.VenueId).OnDelete(DeleteBehavior.Restrict);
    }
}
