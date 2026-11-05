using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketBooking.Infrastructure.Identity;

namespace TicketBooking.Infrastructure.Persistence.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(user => user.DisplayName).IsRequired().HasMaxLength(200);
        builder.HasIndex(user => user.NormalizedEmail).IsUnique();
    }
}
