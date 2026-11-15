using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloria.Data.Migrations
{
    /// <inheritdoc />
    public partial class SettingsAndPlayedAtSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PlayedAtSource",
                table: "Demos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropColumn(
                name: "PlayedAtSource",
                table: "Demos");
        }
    }
}
