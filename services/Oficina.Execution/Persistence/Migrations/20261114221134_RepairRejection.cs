using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oficina.Execution.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RepairRejection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "stock_reservations",
                type: "character varying(180)",
                maxLength: 180,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RepairRejectedAtUtc",
                table: "stock_reservations",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "stock_reservations");

            migrationBuilder.DropColumn(
                name: "RepairRejectedAtUtc",
                table: "stock_reservations");
        }
    }
}
