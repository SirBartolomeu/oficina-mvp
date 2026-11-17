using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OficinaMvp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockReservationSaga : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReservationId",
                table: "work_orders",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReservationId",
                table: "work_orders");
        }
    }
}
