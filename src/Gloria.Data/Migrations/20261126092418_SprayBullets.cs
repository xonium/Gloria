using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloria.Data.Migrations
{
    /// <inheritdoc />
    public partial class SprayBullets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SprayBullets",
                columns: table => new
                {
                    DemoId = table.Column<int>(type: "INTEGER", nullable: false),
                    SteamId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    Weapon = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Bullet = table.Column<int>(type: "INTEGER", nullable: false),
                    Fired = table.Column<int>(type: "INTEGER", nullable: false),
                    Hits = table.Column<int>(type: "INTEGER", nullable: false),
                    Kills = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SprayBullets", x => new { x.DemoId, x.SteamId, x.Weapon, x.Bullet });
                    table.ForeignKey(
                        name: "FK_SprayBullets_Demos_DemoId",
                        column: x => x.DemoId,
                        principalTable: "Demos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SprayBullets_SteamId_DemoId",
                table: "SprayBullets",
                columns: new[] { "SteamId", "DemoId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SprayBullets");
        }
    }
}
