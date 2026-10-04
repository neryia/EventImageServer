using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventImageServer.Migrations
{
    /// <inheritdoc />
    public partial class AddTableAndCategorySide : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Side",
                table: "Tables",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Side",
                table: "GuestCategories",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Side",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "Side",
                table: "GuestCategories");
        }
    }
}
