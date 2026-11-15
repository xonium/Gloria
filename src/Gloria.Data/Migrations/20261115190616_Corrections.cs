using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloria.Data.Migrations
{
    /// <inheritdoc />
    public partial class Corrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Corrections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DemoHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PlayerSteamId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    OpponentSteamId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    RoundNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstShotTick = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Corrections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Corrections_DemoHash_PlayerSteamId_OpponentSteamId_RoundNumber_FirstShotTick_Category",
                table: "Corrections",
                columns: new[] { "DemoHash", "PlayerSteamId", "OpponentSteamId", "RoundNumber", "FirstShotTick", "Category" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Corrections");
        }
    }
}
