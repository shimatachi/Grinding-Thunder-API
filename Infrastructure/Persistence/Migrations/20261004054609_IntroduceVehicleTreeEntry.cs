using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrindingThunder.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntroduceVehicleTreeEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create the new table and indexes first so existing data can be
            //    carried over before the legacy columns are removed.
            migrationBuilder.CreateTable(
                name: "VehicleTreeEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResearchTreeVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RankId = table.Column<Guid>(type: "uuid", nullable: false),
                    RpCost = table.Column<int>(type: "integer", nullable: false),
                    SlCost = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleTreeEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleTreeEntries_Ranks_RankId",
                        column: x => x.RankId,
                        principalTable: "Ranks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VehicleTreeEntries_ResearchTreeVersions_ResearchTreeVersion~",
                        column: x => x.ResearchTreeVersionId,
                        principalTable: "ResearchTreeVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VehicleTreeEntries_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTreeEntries_RankId",
                table: "VehicleTreeEntries",
                column: "RankId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTreeEntries_ResearchTreeVersionId_VehicleId",
                table: "VehicleTreeEntries",
                columns: new[] { "ResearchTreeVersionId", "VehicleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTreeEntries_VehicleId",
                table: "VehicleTreeEntries",
                column: "VehicleId");

            // 2. Data transition: each existing Vehicle's rank, RP cost, and SL cost
            //    become a VehicleTreeEntry in the single research-tree version that
            //    currently exists for the vehicle's tree (the dev-sample version
            //    seeded by the previous migration). Deterministic while each tree
            //    has exactly one version; version-aware placement arrives later.
            //
            //    Safety guard: an inner join to ResearchTreeVersions would silently
            //    drop vehicles whose tree has no version, and multiple versions per
            //    tree would make the target version ambiguous (and violate the new
            //    unique index). Fail the migration loudly in both cases instead of
            //    silently discarding RP/SL/rank state when the legacy columns are
            //    dropped below.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    missingCount integer;
                    ambiguousCount integer;
                BEGIN
                    SELECT COUNT(*) INTO missingCount
                    FROM "Vehicles" v
                    JOIN "Ranks" r ON r."Id" = v."RankId"
                    JOIN "ResearchTrees" rt ON rt."Id" = r."ResearchTreeId"
                    WHERE NOT EXISTS (
                        SELECT 1 FROM "ResearchTreeVersions" rtv
                        WHERE rtv."ResearchTreeId" = rt."Id");

                    IF missingCount > 0 THEN
                        RAISE EXCEPTION
                            'IntroduceVehicleTreeEntry: % vehicle(s) belong to a research tree without any ResearchTreeVersion; their rank/RP/SL state cannot be migrated and would be lost. Create a ResearchTreeVersion for those trees (or remove the orphan vehicles) before applying this migration.',
                            missingCount;
                    END IF;

                    SELECT COUNT(*) INTO ambiguousCount
                    FROM "Vehicles" v
                    JOIN "Ranks" r ON r."Id" = v."RankId"
                    JOIN "ResearchTrees" rt ON rt."Id" = r."ResearchTreeId"
                    WHERE (
                        SELECT COUNT(*) FROM "ResearchTreeVersions" rtv
                        WHERE rtv."ResearchTreeId" = rt."Id"
                    ) > 1;

                    IF ambiguousCount > 0 THEN
                        RAISE EXCEPTION
                            'IntroduceVehicleTreeEntry: % vehicle(s) belong to a research tree with multiple ResearchTreeVersions; the target version is ambiguous. Ensure each tree has exactly one version before applying this migration.',
                            ambiguousCount;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                INSERT INTO "VehicleTreeEntries"
                    ("Id", "ResearchTreeVersionId", "VehicleId", "RankId", "RpCost", "SlCost")
                SELECT gen_random_uuid(), rtv."Id", v."Id", v."RankId", v."RpCost", v."SlCost"
                FROM "Vehicles" v
                JOIN "Ranks" r ON r."Id" = v."RankId"
                JOIN "ResearchTrees" rt ON rt."Id" = r."ResearchTreeId"
                JOIN "ResearchTreeVersions" rtv ON rtv."ResearchTreeId" = rt."Id"
                """);

            // 3. Only after the data is safe, remove the legacy columns.
            migrationBuilder.DropForeignKey(
                name: "FK_Vehicles_Ranks_RankId",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_RankId",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "RankId",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "RpCost",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "SlCost",
                table: "Vehicles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VehicleTreeEntries");

            migrationBuilder.AddColumn<Guid>(
                name: "RankId",
                table: "Vehicles",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "RpCost",
                table: "Vehicles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SlCost",
                table: "Vehicles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_RankId",
                table: "Vehicles",
                column: "RankId");

            migrationBuilder.AddForeignKey(
                name: "FK_Vehicles_Ranks_RankId",
                table: "Vehicles",
                column: "RankId",
                principalTable: "Ranks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
