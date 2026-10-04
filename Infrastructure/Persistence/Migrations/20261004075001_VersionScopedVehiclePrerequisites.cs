using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrindingThunder.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VersionScopedVehiclePrerequisites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Batch 5: prerequisite edges move from stable Vehicle identities to
            // version-scoped VehicleTreeEntry edges. Same-version invariant is
            // enforced by composite FKs to VehicleTreeEntries(ResearchTreeVersionId, Id).

            migrationBuilder.AddUniqueConstraint(
                name: "AK_VehicleTreeEntries_ResearchTreeVersionId_Id",
                table: "VehicleTreeEntries",
                columns: new[] { "ResearchTreeVersionId", "Id" });

            // Old vehicle-based edges table becomes the migration source.
            // Renamed (not dropped) so existing rows are preserved for the
            // guarded copy below; dropped only after every edge is verified
            // migrated.
            migrationBuilder.RenameTable(
                name: "VehiclePrerequisites",
                newName: "VehiclePrerequisites_old");

            // PostgreSQL keeps constraint/index names on table rename; move them
            // aside so the new table can claim the conventional names.
            migrationBuilder.Sql(
                """
                ALTER TABLE "VehiclePrerequisites_old" RENAME CONSTRAINT "PK_VehiclePrerequisites" TO "PK_VehiclePrerequisites_old";
                ALTER TABLE "VehiclePrerequisites_old" RENAME CONSTRAINT "FK_VehiclePrerequisites_Vehicles_VehicleId" TO "FK_VehiclePrerequisites_old_Vehicles_VehicleId";
                ALTER TABLE "VehiclePrerequisites_old" RENAME CONSTRAINT "FK_VehiclePrerequisites_Vehicles_PrerequisiteVehicleId" TO "FK_VehiclePrerequisites_old_Vehicles_PrerequisiteVehicleId";
                ALTER INDEX "IX_VehiclePrerequisites_PrerequisiteVehicleId" RENAME TO "IX_VehiclePrerequisites_old_PrerequisiteVehicleId";
                """);

            migrationBuilder.CreateTable(
                name: "VehiclePrerequisites",
                columns: table => new
                {
                    VehicleTreeEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrerequisiteVehicleTreeEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResearchTreeVersionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehiclePrerequisites", x => new { x.VehicleTreeEntryId, x.PrerequisiteVehicleTreeEntryId });
                    table.CheckConstraint(
                        "CK_VehiclePrerequisites_NoSelfReference",
                        "\"VehicleTreeEntryId\" <> \"PrerequisiteVehicleTreeEntryId\"");
                });

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePrerequisites_ResearchTreeVersionId",
                table: "VehiclePrerequisites",
                column: "ResearchTreeVersionId");

            // ------------------------------------------------------------------
            // GUARDED DATA MIGRATION - no silent loss.
            //
            // Old edges are vehicle-to-vehicle with no version. To map an old
            // edge onto entries, both vehicles' entries must share ONE research
            // tree version. If either vehicle has no entry, or they never share
            // a version, the edge is unmigratable and this migration FAILS
            // EXPLICITLY instead of dropping data. Seed data satisfies this:
            // all seeded vehicles share one published version.
            // ------------------------------------------------------------------
            migrationBuilder.Sql(
                """
                INSERT INTO "VehiclePrerequisites"
                    ("VehicleTreeEntryId", "PrerequisiteVehicleTreeEntryId", "ResearchTreeVersionId")
                SELECT
                    te."Id",
                    pe."Id",
                    te."ResearchTreeVersionId"
                FROM "VehiclePrerequisites_old" AS old
                JOIN LATERAL (
                    SELECT e."Id", e."ResearchTreeVersionId"
                    FROM "VehicleTreeEntries" e
                    WHERE e."VehicleId" = old."VehicleId"
                    INTERSECT
                    SELECT e."Id", e."ResearchTreeVersionId"
                    FROM "VehicleTreeEntries" e
                    WHERE e."VehicleId" = old."PrerequisiteVehicleId"
                ) AS shared ON TRUE
                JOIN LATERAL (
                    SELECT e."Id", e."ResearchTreeVersionId"
                    FROM "VehicleTreeEntries" e
                    WHERE e."VehicleId" = old."PrerequisiteVehicleId"
                      AND e."ResearchTreeVersionId" = shared."ResearchTreeVersionId"
                ) AS pe ON TRUE
                JOIN "VehicleTreeEntries" te
                    ON te."Id" = shared."Id"
                WHERE old."VehicleId" <> old."PrerequisiteVehicleId";
                """);

            // Any old edge that found no shared version was silently skipped by
            // the INSERT above - that would be data loss. Detect leftovers and
            // abort the transaction.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    unmigrated integer;
                BEGIN
                    SELECT COUNT(*) INTO unmigrated
                    FROM "VehiclePrerequisites_old" AS old
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM "VehiclePrerequisites" np
                        JOIN "VehicleTreeEntries" te ON te."Id" = np."VehicleTreeEntryId"
                        JOIN "VehicleTreeEntries" pe ON pe."Id" = np."PrerequisiteVehicleTreeEntryId"
                        WHERE te."VehicleId" = old."VehicleId"
                          AND pe."VehicleId" = old."PrerequisiteVehicleId"
                    );

                    IF unmigrated > 0 THEN
                        RAISE EXCEPTION
                            'VehiclePrerequisite migration aborted: % edge(s) could not be mapped to vehicle tree entries sharing one research tree version. Resolve manually and re-run.',
                            unmigrated;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(name: "VehiclePrerequisites_old");
        }
    }
}
