using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketBooking.Infrastructure.Identity;

namespace TicketBooking.Infrastructure.Persistence.Configurations;

public sealed class RefreshSessionConfiguration : IEntityTypeConfiguration<RefreshSession>
{
    public void Configure(EntityTypeBuilder<RefreshSession> builder)
    {
        builder.ToTable("RefreshSessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();
        builder.Property(session => session.SecurityStampHash).IsRequired().HasMaxLength(64).UseCollation("C");
        builder.HasIndex(session => session.ExpiresAt);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
