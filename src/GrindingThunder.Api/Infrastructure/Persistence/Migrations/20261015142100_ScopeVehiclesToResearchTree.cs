using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrindingThunder.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeVehiclesToResearchTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keep the ownership audit and backfill consistent with concurrent writers.
            // EF executes this migration transactionally; failure rolls back all changes.
            migrationBuilder.Sql(
                """
                LOCK TABLE "Vehicles", "VehicleTreeEntries", "ResearchTreeVersions" IN SHARE ROW EXCLUSIVE MODE;
                DO $$
                DECLARE
                    invalid_count integer;
                    examples text;
                BEGIN
                    SELECT count(*) INTO invalid_count
                    FROM (
                        SELECT vehicle."Id"
                        FROM "Vehicles" vehicle
                        LEFT JOIN "VehicleTreeEntries" entry ON entry."VehicleId" = vehicle."Id"
                        LEFT JOIN "ResearchTreeVersions" version ON version."Id" = entry."ResearchTreeVersionId"
                        GROUP BY vehicle."Id"
                        HAVING count(DISTINCT version."ResearchTreeId") <> 1
                    ) invalid;

                    IF invalid_count > 0 THEN
                        SELECT string_agg(detail, '; ') INTO examples
                        FROM (
                            SELECT format('%s "%s" trees=[%s]', vehicle."Id", vehicle."Name",
                                coalesce(string_agg(DISTINCT version."ResearchTreeId"::text, ', '), 'none')) AS detail
                            FROM "Vehicles" vehicle
                            LEFT JOIN "VehicleTreeEntries" entry ON entry."VehicleId" = vehicle."Id"
                            LEFT JOIN "ResearchTreeVersions" version ON version."Id" = entry."ResearchTreeVersionId"
                            GROUP BY vehicle."Id", vehicle."Name"
                            HAVING count(DISTINCT version."ResearchTreeId") <> 1
                            ORDER BY vehicle."Id"
                            LIMIT 20
                        ) sample;
                        RAISE EXCEPTION 'Vehicle ownership migration aborted: % unowned or multi-tree vehicle(s). First 20: %. Resolve ownership explicitly before retrying; no vehicle or player records were changed.',
                            invalid_count, examples;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_VehicleTreeEntries_ResearchTreeVersions_ResearchTreeVersion~",
                table: "VehicleTreeEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_VehicleTreeEntries_Vehicles_VehicleId",
                table: "VehicleTreeEntries");

            migrationBuilder.DropIndex(
                name: "IX_VehicleTreeEntries_VehicleId",
                table: "VehicleTreeEntries");

            migrationBuilder.AddColumn<Guid>(
                name: "ResearchTreeId",
                table: "VehicleTreeEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResearchTreeId",
                table: "Vehicles",
                type: "uuid",
                nullable: true);

            // The audit proved that all source rows for each vehicle share one tree.
            migrationBuilder.Sql(
                """
                UPDATE "Vehicles" vehicle
                SET "ResearchTreeId" = version."ResearchTreeId"
                FROM "VehicleTreeEntries" entry
                JOIN "ResearchTreeVersions" version ON version."Id" = entry."ResearchTreeVersionId"
                WHERE entry."VehicleId" = vehicle."Id";

                UPDATE "VehicleTreeEntries" entry
                SET "ResearchTreeId" = version."ResearchTreeId"
                FROM "ResearchTreeVersions" version
                WHERE version."Id" = entry."ResearchTreeVersionId";

                DO $$
                DECLARE
                    duplicate_count integer;
                    examples text;
                BEGIN
                    SELECT count(*) INTO duplicate_count
                    FROM (
                        SELECT "ResearchTreeId", lower(trim("Name"))
                        FROM "Vehicles"
                        GROUP BY "ResearchTreeId", lower(trim("Name"))
                        HAVING count(*) > 1
                    ) duplicates;
                    IF duplicate_count > 0 THEN
                        SELECT string_agg(detail, '; ') INTO examples
                        FROM (
                            SELECT format('tree=%s name="%s" vehicle IDs=[%s]', "ResearchTreeId",
                                lower(trim("Name")), string_agg("Id"::text, ', ' ORDER BY "Id")) AS detail
                            FROM "Vehicles"
                            GROUP BY "ResearchTreeId", lower(trim("Name"))
                            HAVING count(*) > 1
                            ORDER BY "ResearchTreeId", lower(trim("Name"))
                            LIMIT 20
                        ) sample;
                        RAISE EXCEPTION 'Vehicle ownership migration aborted: % duplicate normalized name group(s) within a tree. First 20: %. Resolve names explicitly; do not delete or merge player ownership to bypass this guard.',
                            duplicate_count, examples;
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "ResearchTreeId", table: "Vehicles", type: "uuid", nullable: false,
                oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
            migrationBuilder.AlterColumn<Guid>(
                name: "ResearchTreeId", table: "VehicleTreeEntries", type: "uuid", nullable: false,
                oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "Vehicles",
                type: "text",
                nullable: true,
                computedColumnSql: "lower(trim(\"Name\"))",
                stored: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Vehicles_ResearchTreeId_Id",
                table: "Vehicles",
                columns: new[] { "ResearchTreeId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ResearchTreeVersions_ResearchTreeId_Id",
                table: "ResearchTreeVersions",
                columns: new[] { "ResearchTreeId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTreeEntries_ResearchTreeId_ResearchTreeVersionId",
                table: "VehicleTreeEntries",
                columns: new[] { "ResearchTreeId", "ResearchTreeVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTreeEntries_ResearchTreeId_VehicleId",
                table: "VehicleTreeEntries",
                columns: new[] { "ResearchTreeId", "VehicleId" });

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_ResearchTreeId_NormalizedName",
                table: "Vehicles",
                columns: new[] { "ResearchTreeId", "NormalizedName" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Vehicles_ResearchTrees_ResearchTreeId",
                table: "Vehicles",
                column: "ResearchTreeId",
                principalTable: "ResearchTrees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleTreeEntries_ResearchTreeVersions_ResearchTreeId_Rese~",
                table: "VehicleTreeEntries",
                columns: new[] { "ResearchTreeId", "ResearchTreeVersionId" },
                principalTable: "ResearchTreeVersions",
                principalColumns: new[] { "ResearchTreeId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleTreeEntries_Vehicles_ResearchTreeId_VehicleId",
                table: "VehicleTreeEntries",
                columns: new[] { "ResearchTreeId", "VehicleId" },
                principalTable: "Vehicles",
                principalColumns: new[] { "ResearchTreeId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                LOCK TABLE "Vehicles", "VehicleTreeEntries", "ResearchTreeVersions" IN SHARE ROW EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "Vehicles" vehicle
                        WHERE NOT EXISTS (SELECT 1 FROM "VehicleTreeEntries" entry WHERE entry."VehicleId" = vehicle."Id")
                    ) THEN
                        RAISE EXCEPTION 'Vehicle ownership migration cannot be reversed: entryless vehicles would lose their only ownership mapping. Preserve an explicit mapping before downgrading.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Vehicles_ResearchTrees_ResearchTreeId",
                table: "Vehicles");

            migrationBuilder.DropForeignKey(
                name: "FK_VehicleTreeEntries_ResearchTreeVersions_ResearchTreeId_Rese~",
                table: "VehicleTreeEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_VehicleTreeEntries_Vehicles_ResearchTreeId_VehicleId",
                table: "VehicleTreeEntries");

            migrationBuilder.DropIndex(
                name: "IX_VehicleTreeEntries_ResearchTreeId_ResearchTreeVersionId",
                table: "VehicleTreeEntries");

            migrationBuilder.DropIndex(
                name: "IX_VehicleTreeEntries_ResearchTreeId_VehicleId",
                table: "VehicleTreeEntries");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Vehicles_ResearchTreeId_Id",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_ResearchTreeId_NormalizedName",
                table: "Vehicles");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ResearchTreeVersions_ResearchTreeId_Id",
                table: "ResearchTreeVersions");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "ResearchTreeId",
                table: "VehicleTreeEntries");

            migrationBuilder.DropColumn(
                name: "ResearchTreeId",
                table: "Vehicles");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTreeEntries_VehicleId",
                table: "VehicleTreeEntries",
                column: "VehicleId");

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleTreeEntries_ResearchTreeVersions_ResearchTreeVersion~",
                table: "VehicleTreeEntries",
                column: "ResearchTreeVersionId",
                principalTable: "ResearchTreeVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleTreeEntries_Vehicles_VehicleId",
                table: "VehicleTreeEntries",
                column: "VehicleId",
                principalTable: "Vehicles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
