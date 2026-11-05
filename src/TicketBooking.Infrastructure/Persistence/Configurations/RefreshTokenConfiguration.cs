using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketBooking.Infrastructure.Identity;

namespace TicketBooking.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedNever();
        builder.Property(token => token.TokenHash).IsRequired().HasMaxLength(64).UseCollation("C");
        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => token.SessionId);
        builder.HasIndex(token => token.SessionId, "UnconsumedRefreshTokenPerSession")
            .HasDatabaseName("IX_RefreshTokens_UnconsumedSession").IsUnique().HasFilter("\"ConsumedAt\" IS NULL");
        builder.HasOne<RefreshSession>().WithMany().HasForeignKey(token => token.SessionId).OnDelete(DeleteBehavior.Cascade);
    }
}
