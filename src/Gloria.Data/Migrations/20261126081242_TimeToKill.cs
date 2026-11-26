using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloria.Data.Migrations
{
    /// <inheritdoc />
    public partial class TimeToKill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "TimeToKill",
                table: "Encounters",
                type: "REAL",
                nullable: true);

            // Kills already stored: the encounter ends on the kill tick, so this needs no re-analysis.
            // Same rule as Gloria.Core.Encounters.TimeToKill.Seconds; Outcome 0 is Kill.
            migrationBuilder.Sql(
                """
                UPDATE Encounters
                SET TimeToKill = (EndTick - FirstShotTick) * 1.0 / (SELECT TickRate FROM Demos WHERE Demos.Id = Encounters.DemoId)
                WHERE Outcome = 0
                  AND EndTick >= FirstShotTick
                  AND (SELECT TickRate FROM Demos WHERE Demos.Id = Encounters.DemoId) > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeToKill",
                table: "Encounters");
        }
    }
}
