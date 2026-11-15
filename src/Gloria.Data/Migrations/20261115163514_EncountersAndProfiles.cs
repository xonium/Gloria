using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloria.Data.Migrations
{
    /// <inheritdoc />
    public partial class EncountersAndProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnalysisVersionKey",
                table: "Demos",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FilePath",
                table: "Demos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSize",
                table: "Demos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "TickRate",
                table: "Demos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TeamsByRound",
                table: "DemoPlayers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Encounters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DemoId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerSteamId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    OpponentSteamId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    RoundNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    StartTick = table.Column<int>(type: "INTEGER", nullable: false),
                    EndTick = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    Approach = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeToFirstShot = table.Column<double>(type: "REAL", nullable: true),
                    TimeToOnTarget = table.Column<double>(type: "REAL", nullable: true),
                    FirstShotTick = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstShotWeapon = table.Column<string>(type: "TEXT", nullable: false),
                    OpponentWeapon = table.Column<string>(type: "TEXT", nullable: false),
                    ShotCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ShotsWhileMoving = table.Column<int>(type: "INTEGER", nullable: false),
                    SpeedAtFirstShot = table.Column<float>(type: "REAL", nullable: false),
                    SpeedRatioAtFirstShot = table.Column<float>(type: "REAL", nullable: false),
                    AngularErrorAtStart = table.Column<float>(type: "REAL", nullable: false),
                    AngularErrorAtFirstShot = table.Column<float>(type: "REAL", nullable: false),
                    PeakOvershootDegrees = table.Column<float>(type: "REAL", nullable: false),
                    CorrectionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FlickDistanceDegrees = table.Column<float>(type: "REAL", nullable: false),
                    DistanceAtFirstShot = table.Column<float>(type: "REAL", nullable: false),
                    PlayerHealthAtStart = table.Column<int>(type: "INTEGER", nullable: false),
                    OpponentHealthAtStart = table.Column<int>(type: "INTEGER", nullable: false),
                    FiredFirst = table.Column<bool>(type: "INTEGER", nullable: false),
                    FirstShotHit = table.Column<bool>(type: "INTEGER", nullable: true),
                    WonDuel = table.Column<bool>(type: "INTEGER", nullable: true),
                    Flags = table.Column<int>(type: "INTEGER", nullable: false),
                    PrimaryCategory = table.Column<int>(type: "INTEGER", nullable: true),
                    ClassifierSource = table.Column<string>(type: "TEXT", nullable: false),
                    OvershootProbability = table.Column<float>(type: "REAL", nullable: false),
                    UndershootProbability = table.Column<float>(type: "REAL", nullable: false),
                    MovingShotProbability = table.Column<float>(type: "REAL", nullable: false),
                    HastyProbability = table.Column<float>(type: "REAL", nullable: false),
                    SlowReactionProbability = table.Column<float>(type: "REAL", nullable: false),
                    Trace = table.Column<byte[]>(type: "BLOB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Encounters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Encounters_Demos_DemoId",
                        column: x => x.DemoId,
                        principalTable: "Demos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Profiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SteamId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rounds",
                columns: table => new
                {
                    DemoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    StartTick = table.Column<int>(type: "INTEGER", nullable: false),
                    FreezeEndTick = table.Column<int>(type: "INTEGER", nullable: false),
                    EndTick = table.Column<int>(type: "INTEGER", nullable: false),
                    Winner = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rounds", x => new { x.DemoId, x.Number });
                    table.ForeignKey(
                        name: "FK_Rounds_Demos_DemoId",
                        column: x => x.DemoId,
                        principalTable: "Demos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Encounters_DemoId",
                table: "Encounters",
                column: "DemoId");

            migrationBuilder.CreateIndex(
                name: "IX_Encounters_PlayerSteamId_DemoId",
                table: "Encounters",
                columns: new[] { "PlayerSteamId", "DemoId" });

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_SteamId",
                table: "Profiles",
                column: "SteamId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Encounters");

            migrationBuilder.DropTable(
                name: "Profiles");

            migrationBuilder.DropTable(
                name: "Rounds");

            migrationBuilder.DropColumn(
                name: "AnalysisVersionKey",
                table: "Demos");

            migrationBuilder.DropColumn(
                name: "FilePath",
                table: "Demos");

            migrationBuilder.DropColumn(
                name: "FileSize",
                table: "Demos");

            migrationBuilder.DropColumn(
                name: "TickRate",
                table: "Demos");

            migrationBuilder.DropColumn(
                name: "TeamsByRound",
                table: "DemoPlayers");
        }
    }
}
