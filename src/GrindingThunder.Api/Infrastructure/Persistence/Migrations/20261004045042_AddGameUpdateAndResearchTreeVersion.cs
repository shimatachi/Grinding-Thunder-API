using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrindingThunder.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGameUpdateAndResearchTreeVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameUpdates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReleaseDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameUpdates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResearchTreeVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResearchTreeId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameUpdateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchTreeVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResearchTreeVersions_GameUpdates_GameUpdateId",
                        column: x => x.GameUpdateId,
                        principalTable: "GameUpdates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ResearchTreeVersions_ResearchTrees_ResearchTreeId",
                        column: x => x.ResearchTreeId,
                        principalTable: "ResearchTrees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameUpdates_Version",
                table: "GameUpdates",
                column: "Version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResearchTreeVersions_GameUpdateId",
                table: "ResearchTreeVersions",
                column: "GameUpdateId");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchTreeVersions_ResearchTreeId_GameUpdateId",
                table: "ResearchTreeVersions",
                columns: new[] { "ResearchTreeId", "GameUpdateId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResearchTreeVersions");

            migrationBuilder.DropTable(
                name: "GameUpdates");
        }
    }
}
