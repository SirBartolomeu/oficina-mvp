using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OficinaMvp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProcessStateEnteredAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessStateEnteredAtUtc",
                table: "work_orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProcessStateEnteredAtUtc",
                table: "work_orders");
        }
    }
}
