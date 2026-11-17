using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OficinaMvp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerActiveStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "customers",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "customers");
        }
    }
}
