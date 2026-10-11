using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrindingThunder.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RestrictResearchTreeVersionCascadeDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ResearchTreeVersions_GameUpdates_GameUpdateId",
                table: "ResearchTreeVersions");

            migrationBuilder.DropForeignKey(
                name: "FK_ResearchTreeVersions_ResearchTrees_ResearchTreeId",
                table: "ResearchTreeVersions");

            migrationBuilder.AddForeignKey(
                name: "FK_ResearchTreeVersions_GameUpdates_GameUpdateId",
                table: "ResearchTreeVersions",
                column: "GameUpdateId",
                principalTable: "GameUpdates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ResearchTreeVersions_ResearchTrees_ResearchTreeId",
                table: "ResearchTreeVersions",
                column: "ResearchTreeId",
                principalTable: "ResearchTrees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ResearchTreeVersions_GameUpdates_GameUpdateId",
                table: "ResearchTreeVersions");

            migrationBuilder.DropForeignKey(
                name: "FK_ResearchTreeVersions_ResearchTrees_ResearchTreeId",
                table: "ResearchTreeVersions");

            migrationBuilder.AddForeignKey(
                name: "FK_ResearchTreeVersions_GameUpdates_GameUpdateId",
                table: "ResearchTreeVersions",
                column: "GameUpdateId",
                principalTable: "GameUpdates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ResearchTreeVersions_ResearchTrees_ResearchTreeId",
                table: "ResearchTreeVersions",
                column: "ResearchTreeId",
                principalTable: "ResearchTrees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
