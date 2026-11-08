using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketBooking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHoldItemAllocationVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AllocationVersion",
                table: "SeatHoldItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SeatHoldItems_AllocationVersion_Positive",
                table: "SeatHoldItems",
                sql: "\"AllocationVersion\" IS NULL OR \"AllocationVersion\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SeatHoldItems_AllocationVersion_Positive",
                table: "SeatHoldItems");

            migrationBuilder.DropColumn(
                name: "AllocationVersion",
                table: "SeatHoldItems");
        }
    }
}
