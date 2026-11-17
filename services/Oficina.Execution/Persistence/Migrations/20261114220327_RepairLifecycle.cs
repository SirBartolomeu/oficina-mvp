using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oficina.Execution.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RepairLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BranchId",
                table: "stock_reservations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentAttemptId",
                table: "stock_reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RepairFinishedAtUtc",
                table: "stock_reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RepairStartedAtUtc",
                table: "stock_reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepairStatus",
                table: "stock_reservations",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "stock_reservations");

            migrationBuilder.DropColumn(
                name: "PaymentAttemptId",
                table: "stock_reservations");

            migrationBuilder.DropColumn(
                name: "RepairFinishedAtUtc",
                table: "stock_reservations");

            migrationBuilder.DropColumn(
                name: "RepairStartedAtUtc",
                table: "stock_reservations");

            migrationBuilder.DropColumn(
                name: "RepairStatus",
                table: "stock_reservations");
        }
    }
}
