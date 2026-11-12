using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketBooking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceUniqueBookingSourceHold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceHoldId",
                table: "Bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_SourceHoldId",
                table: "Bookings",
                column: "SourceHoldId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_SeatHolds_SourceHoldId",
                table: "Bookings",
                column: "SourceHoldId",
                principalTable: "SeatHolds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_SeatHolds_SourceHoldId",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_SourceHoldId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "SourceHoldId",
                table: "Bookings");
        }
    }
}
