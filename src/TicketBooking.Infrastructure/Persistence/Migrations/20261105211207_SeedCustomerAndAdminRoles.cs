using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketBooking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedCustomerAndAdminRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve pre-existing role IDs and memberships while enforcing canonical names.
            migrationBuilder.Sql("""
                INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
                VALUES
                    ('58c86a52-964c-4d59-8b28-eb3df0dfc1a1', 'Customer', 'CUSTOMER', 'c3b054a1-709a-4091-8b0e-4d6ed6e724a1'),
                    ('58c86a52-964c-4d59-8b28-eb3df0dfc1a2', 'Admin', 'ADMIN', 'c3b054a1-709a-4091-8b0e-4d6ed6e724a2')
                ON CONFLICT ("NormalizedName") DO UPDATE
                SET "Name" = EXCLUDED."Name", "ConcurrencyStamp" = EXCLUDED."ConcurrencyStamp";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Retain roles: they may predate this migration and may now have memberships.
        }
    }
}
