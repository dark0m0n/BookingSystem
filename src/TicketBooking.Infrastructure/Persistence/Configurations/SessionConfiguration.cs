using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Entity = TicketBooking.Domain.Catalogue.Session;

namespace TicketBooking.Infrastructure.Persistence.Configurations;

public sealed class SessionConfiguration : IEntityTypeConfiguration<Entity>
{
    public void Configure(EntityTypeBuilder<Entity> builder)
    {
        builder.ToTable("Sessions");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.HasOne<TicketBooking.Domain.Catalogue.Event>().WithMany()
            .HasForeignKey(session => session.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TicketBooking.Domain.Venue.Auditorium>().WithMany()
            .HasForeignKey(session => session.AuditoriumId).OnDelete(DeleteBehavior.Restrict);
    }
}
