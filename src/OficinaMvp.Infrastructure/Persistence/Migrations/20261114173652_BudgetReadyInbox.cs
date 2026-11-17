using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OficinaMvp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BudgetReadyInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BudgetId",
                table: "work_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BudgetVersion",
                table: "work_orders",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_messages", x => x.EventId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropColumn(
                name: "BudgetId",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "BudgetVersion",
                table: "work_orders");
        }
    }
}
