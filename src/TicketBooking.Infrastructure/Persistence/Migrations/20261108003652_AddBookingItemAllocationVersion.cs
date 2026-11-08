using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketBooking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingItemAllocationVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AllocationVersion",
                table: "BookingItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BookingItems_AllocationVersion_Positive",
                table: "BookingItems",
                sql: "\"AllocationVersion\" IS NULL OR \"AllocationVersion\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BookingItems_AllocationVersion_Positive",
                table: "BookingItems");

            migrationBuilder.DropColumn(
                name: "AllocationVersion",
                table: "BookingItems");
        }
    }
}
