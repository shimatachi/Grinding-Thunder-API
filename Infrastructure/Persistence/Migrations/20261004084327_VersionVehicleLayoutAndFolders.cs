using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrindingThunder.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VersionVehicleLayoutAndFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FolderParentEntryId",
                table: "VehicleTreeEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TreeColumn",
                table: "VehicleTreeEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TreeRow",
                table: "VehicleTreeEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Copy stable-vehicle layout values to every versioned placement.
            migrationBuilder.Sql(
                """
                UPDATE "VehicleTreeEntries" AS entry
                SET "TreeColumn" = vehicle."TreeColumn",
                    "TreeRow" = vehicle."TreeRow"
                FROM "Vehicles" AS vehicle
                WHERE vehicle."Id" = entry."VehicleId";
                """);

            // A legacy vehicle relationship maps only to entries in the exact
            // same version. Missing child entries, missing same-version parent
            // entries, or inconsistent legacy marker data abort the migration
            // before any old columns are removed.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    unmappable_relationships integer;
                    inconsistent_markers integer;
                BEGIN
                    SELECT COUNT(*) INTO unmappable_relationships
                    FROM "Vehicles" AS child_vehicle
                    WHERE child_vehicle."FolderParentId" IS NOT NULL
                      AND (
                          NOT EXISTS (
                              SELECT 1
                              FROM "VehicleTreeEntries" AS child_entry
                              WHERE child_entry."VehicleId" = child_vehicle."Id"
                          )
                          OR EXISTS (
                              SELECT 1
                              FROM "VehicleTreeEntries" AS child_entry
                              WHERE child_entry."VehicleId" = child_vehicle."Id"
                                AND NOT EXISTS (
                                    SELECT 1
                                    FROM "VehicleTreeEntries" AS parent_entry
                                    WHERE parent_entry."VehicleId" = child_vehicle."FolderParentId"
                                      AND parent_entry."ResearchTreeVersionId" = child_entry."ResearchTreeVersionId"
                                )
                          )
                      );

                    IF unmappable_relationships > 0 THEN
                        RAISE EXCEPTION
                            'Vehicle layout migration aborted: % legacy folder relationship(s) cannot be mapped to entries in the same research tree version.',
                            unmappable_relationships;
                    END IF;

                    SELECT COUNT(*) INTO inconsistent_markers
                    FROM "Vehicles" AS parent_vehicle
                    WHERE parent_vehicle."IsFolderParent" <>
                          EXISTS (
                              SELECT 1
                              FROM "Vehicles" AS child_vehicle
                              WHERE child_vehicle."FolderParentId" = parent_vehicle."Id"
                          );

                    IF inconsistent_markers > 0 THEN
                        RAISE EXCEPTION
                            'Vehicle layout migration aborted: % IsFolderParent marker(s) disagree with legacy folder relationships.',
                            inconsistent_markers;
                    END IF;
                END $$;

                UPDATE "VehicleTreeEntries" AS child_entry
                SET "FolderParentEntryId" = parent_entry."Id"
                FROM "Vehicles" AS child_vehicle,
                     "VehicleTreeEntries" AS parent_entry
                WHERE child_entry."VehicleId" = child_vehicle."Id"
                  AND child_vehicle."FolderParentId" IS NOT NULL
                  AND parent_entry."VehicleId" = child_vehicle."FolderParentId"
                  AND parent_entry."ResearchTreeVersionId" = child_entry."ResearchTreeVersionId";

                DO $$
                DECLARE
                    unpreserved_parent_markers integer;
                BEGIN
                    SELECT COUNT(*) INTO unpreserved_parent_markers
                    FROM "VehicleTreeEntries" AS parent_entry
                    JOIN "Vehicles" AS parent_vehicle
                      ON parent_vehicle."Id" = parent_entry."VehicleId"
                    WHERE parent_vehicle."IsFolderParent"
                      AND NOT EXISTS (
                          SELECT 1
                          FROM "VehicleTreeEntries" AS child_entry
                          WHERE child_entry."FolderParentEntryId" = parent_entry."Id"
                            AND child_entry."ResearchTreeVersionId" = parent_entry."ResearchTreeVersionId"
                      );

                    IF unpreserved_parent_markers > 0 THEN
                        RAISE EXCEPTION
                            'Vehicle layout migration aborted: % folder-parent marker(s) have no child entry in the same research tree version.',
                            unpreserved_parent_markers;
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTreeEntries_ResearchTreeVersionId_FolderParentEntryId",
                table: "VehicleTreeEntries",
                columns: new[] { "ResearchTreeVersionId", "FolderParentEntryId" });

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleTreeEntries_VehicleTreeEntries_ResearchTreeVersionId~",
                table: "VehicleTreeEntries",
                columns: new[] { "ResearchTreeVersionId", "FolderParentEntryId" },
                principalTable: "VehicleTreeEntries",
                principalColumns: new[] { "ResearchTreeVersionId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropForeignKey(
                name: "FK_Vehicles_Vehicles_FolderParentId",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_FolderParentId",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "FolderParentId",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "IsFolderParent",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "TreeColumn",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "TreeRow",
                table: "Vehicles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VehicleTreeEntries_VehicleTreeEntries_ResearchTreeVersionId~",
                table: "VehicleTreeEntries");

            migrationBuilder.DropIndex(
                name: "IX_VehicleTreeEntries_ResearchTreeVersionId_FolderParentEntryId",
                table: "VehicleTreeEntries");

            migrationBuilder.AddColumn<Guid>(
                name: "FolderParentId",
                table: "Vehicles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFolderParent",
                table: "Vehicles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TreeColumn",
                table: "Vehicles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TreeRow",
                table: "Vehicles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Downgrading can represent only one layout per stable vehicle.
            // Refuse to choose an arbitrary version if snapshots have diverged.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    ambiguous_vehicles integer;
                BEGIN
                    SELECT COUNT(*) INTO ambiguous_vehicles
                    FROM (
                        SELECT entry."VehicleId"
                        FROM "VehicleTreeEntries" AS entry
                        LEFT JOIN "VehicleTreeEntries" AS parent_entry
                          ON parent_entry."ResearchTreeVersionId" = entry."ResearchTreeVersionId"
                         AND parent_entry."Id" = entry."FolderParentEntryId"
                        GROUP BY entry."VehicleId"
                        HAVING COUNT(DISTINCT entry."TreeColumn") > 1
                            OR COUNT(DISTINCT entry."TreeRow") > 1
                            OR COUNT(DISTINCT COALESCE(parent_entry."VehicleId", '00000000-0000-0000-0000-000000000000'::uuid)) > 1
                    ) AS ambiguous;

                    IF ambiguous_vehicles > 0 THEN
                        RAISE EXCEPTION
                            'Vehicle layout migration cannot be reversed: % vehicle(s) have version-specific layout or folder membership.',
                            ambiguous_vehicles;
                    END IF;
                END $$;

                UPDATE "Vehicles" AS vehicle
                SET "TreeColumn" = entry."TreeColumn",
                    "TreeRow" = entry."TreeRow",
                    "FolderParentId" = parent_entry."VehicleId"
                FROM "VehicleTreeEntries" AS entry
                LEFT JOIN "VehicleTreeEntries" AS parent_entry
                  ON parent_entry."ResearchTreeVersionId" = entry."ResearchTreeVersionId"
                 AND parent_entry."Id" = entry."FolderParentEntryId"
                WHERE entry."VehicleId" = vehicle."Id";

                UPDATE "Vehicles" AS vehicle
                SET "IsFolderParent" = EXISTS (
                    SELECT 1
                    FROM "VehicleTreeEntries" AS parent_entry
                    JOIN "VehicleTreeEntries" AS child_entry
                      ON child_entry."ResearchTreeVersionId" = parent_entry."ResearchTreeVersionId"
                     AND child_entry."FolderParentEntryId" = parent_entry."Id"
                    WHERE parent_entry."VehicleId" = vehicle."Id"
                );
                """);

            migrationBuilder.DropColumn(
                name: "FolderParentEntryId",
                table: "VehicleTreeEntries");

            migrationBuilder.DropColumn(
                name: "TreeColumn",
                table: "VehicleTreeEntries");

            migrationBuilder.DropColumn(
                name: "TreeRow",
                table: "VehicleTreeEntries");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_FolderParentId",
                table: "Vehicles",
                column: "FolderParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Vehicles_Vehicles_FolderParentId",
                table: "Vehicles",
                column: "FolderParentId",
                principalTable: "Vehicles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
