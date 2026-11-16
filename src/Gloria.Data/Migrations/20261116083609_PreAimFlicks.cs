using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloria.Data.Migrations
{
    /// <inheritdoc />
    public partial class PreAimFlicks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApproachStartTick",
                table: "Encounters",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<float>(
                name: "LargeFlickProbability",
                table: "Encounters",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "MediumFlickProbability",
                table: "Encounters",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "PreAimErrorDegrees",
                table: "Encounters",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "SmallFlickProbability",
                table: "Encounters",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApproachStartTick",
                table: "Encounters");

            migrationBuilder.DropColumn(
                name: "LargeFlickProbability",
                table: "Encounters");

            migrationBuilder.DropColumn(
                name: "MediumFlickProbability",
                table: "Encounters");

            migrationBuilder.DropColumn(
                name: "PreAimErrorDegrees",
                table: "Encounters");

            migrationBuilder.DropColumn(
                name: "SmallFlickProbability",
                table: "Encounters");
        }
    }
}
