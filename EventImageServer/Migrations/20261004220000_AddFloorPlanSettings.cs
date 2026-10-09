using EventImageServer.Contexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventImageServer.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261004220000_AddFloorPlanSettings")]
    public partial class AddFloorPlanSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FloorPlanHeight",
                table: "Clients",
                type: "INTEGER",
                nullable: false,
                defaultValue: 600);

            migrationBuilder.AddColumn<int>(
                name: "FloorPlanWidth",
                table: "Clients",
                type: "INTEGER",
                nullable: false,
                defaultValue: 900);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FloorPlanHeight",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "FloorPlanWidth",
                table: "Clients");
        }
    }
}
