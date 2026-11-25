using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloria.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlayerMatchStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Damage",
                table: "DemoPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Deaths",
                table: "DemoPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HeadshotKills",
                table: "DemoPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kills",
                table: "DemoPlayers",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Damage",
                table: "DemoPlayers");

            migrationBuilder.DropColumn(
                name: "Deaths",
                table: "DemoPlayers");

            migrationBuilder.DropColumn(
                name: "HeadshotKills",
                table: "DemoPlayers");

            migrationBuilder.DropColumn(
                name: "Kills",
                table: "DemoPlayers");
        }
    }
}
