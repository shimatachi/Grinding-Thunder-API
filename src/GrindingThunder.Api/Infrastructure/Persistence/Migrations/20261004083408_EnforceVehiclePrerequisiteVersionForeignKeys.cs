using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrindingThunder.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceVehiclePrerequisiteVersionForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The previous migration added the composite alternate key that
            // these relationships target, but omitted the foreign-key
            // constraints from the physical table. Sharing the version column
            // between both FKs makes it impossible for an edge's endpoints to
            // resolve to entries from different research-tree versions.
            migrationBuilder.AddForeignKey(
                name: "FK_VehiclePrerequisites_VehicleTreeEntries_ResearchTreeVersionId_PrerequisiteVehicleTreeEntryId",
                table: "VehiclePrerequisites",
                columns: new[] { "ResearchTreeVersionId", "PrerequisiteVehicleTreeEntryId" },
                principalTable: "VehicleTreeEntries",
                principalColumns: new[] { "ResearchTreeVersionId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VehiclePrerequisites_VehicleTreeEntries_ResearchTreeVersio~1",
                table: "VehiclePrerequisites",
                columns: new[] { "ResearchTreeVersionId", "VehicleTreeEntryId" },
                principalTable: "VehicleTreeEntries",
                principalColumns: new[] { "ResearchTreeVersionId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VehiclePrerequisites_VehicleTreeEntries_ResearchTreeVersionId_PrerequisiteVehicleTreeEntryId",
                table: "VehiclePrerequisites");

            migrationBuilder.DropForeignKey(
                name: "FK_VehiclePrerequisites_VehicleTreeEntries_ResearchTreeVersio~1",
                table: "VehiclePrerequisites");
        }
    }
}
