using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloria.Data.Migrations
{
    /// <inheritdoc />
    public partial class CorrectionKeyWithoutRound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Corrections_DemoHash_PlayerSteamId_OpponentSteamId_RoundNumber_FirstShotTick_Category",
                table: "Corrections");

            migrationBuilder.CreateIndex(
                name: "IX_Corrections_DemoHash_PlayerSteamId_OpponentSteamId_FirstShotTick_Category",
                table: "Corrections",
                columns: new[] { "DemoHash", "PlayerSteamId", "OpponentSteamId", "FirstShotTick", "Category" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Corrections_DemoHash_PlayerSteamId_OpponentSteamId_FirstShotTick_Category",
                table: "Corrections");

            migrationBuilder.CreateIndex(
                name: "IX_Corrections_DemoHash_PlayerSteamId_OpponentSteamId_RoundNumber_FirstShotTick_Category",
                table: "Corrections",
                columns: new[] { "DemoHash", "PlayerSteamId", "OpponentSteamId", "RoundNumber", "FirstShotTick", "Category" },
                unique: true);
        }
    }
}
