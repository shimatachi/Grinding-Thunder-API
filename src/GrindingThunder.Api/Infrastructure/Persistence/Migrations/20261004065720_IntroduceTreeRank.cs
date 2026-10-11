using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrindingThunder.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntroduceTreeRank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create TreeRanks (owned by ResearchTreeVersion) BEFORE touching
            //    the old Ranks table so existing rank data can be carried over.
            migrationBuilder.CreateTable(
                name: "TreeRanks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResearchTreeVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RankNumber = table.Column<int>(type: "integer", nullable: false),
                    RequiredVehiclesUnlocked = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreeRanks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TreeRanks_ResearchTreeVersions_ResearchTreeVersionId",
                        column: x => x.ResearchTreeVersionId,
                        principalTable: "ResearchTreeVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TreeRanks_ResearchTreeVersionId_RankNumber",
                table: "TreeRanks",
                columns: new[] { "ResearchTreeVersionId", "RankNumber" },
                unique: true);

            // 2. Data transition: every Rank is copied once per version of its
            //    research tree (rank configuration becomes version-specific).
            //
            //    Safety guard: if a tree has NO version, an inner join would
            //    silently lose its ranks. Fail explicitly instead.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    orphanRankCount integer;
                BEGIN
                    SELECT COUNT(*) INTO orphanRankCount
                    FROM "Ranks" r
                    WHERE NOT EXISTS (
                        SELECT 1 FROM "ResearchTreeVersions" rtv
                        JOIN "ResearchTrees" rt ON rt."Id" = rtv."ResearchTreeId"
                        WHERE rt."Id" = r."ResearchTreeId"
                    );

                    IF orphanRankCount > 0 THEN
                        RAISE EXCEPTION
                            'IntroduceTreeRank: % rank(s) belong to a research tree without any ResearchTreeVersion; there is no version to copy them into. Create a version for every tree before applying this migration.',
                            orphanRankCount;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                INSERT INTO "TreeRanks"
                    ("Id", "ResearchTreeVersionId", "RankNumber", "RequiredVehiclesUnlocked")
                SELECT gen_random_uuid(), rtv."Id", r."RankNumber", r."RequiredVehiclesUnlocked"
                FROM "Ranks" r
                JOIN "ResearchTrees" rt ON rt."Id" = r."ResearchTreeId"
                JOIN "ResearchTreeVersions" rtv ON rtv."ResearchTreeId" = rt."Id"
                """);

            // 3. Remap each VehicleTreeEntry to the TreeRank of the SAME version
            //    with the SAME rank number. A version may only hold one rank per
            //    number once the unique index exists, so the lookup is
            //    deterministic when the entry and rank share a tree. The guard
            //    below fails explicitly on any non-deterministic mapping
            //    (entry whose rank does not resolve to exactly one TreeRank).
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    ambiguousCount integer;
                BEGIN
                    SELECT COUNT(*) INTO ambiguousCount
                    FROM "VehicleTreeEntries" e
                    JOIN "Ranks" r ON r."Id" = e."RankId"
                    JOIN "ResearchTrees" rt ON rt."Id" = r."ResearchTreeId"
                    WHERE (
                        SELECT COUNT(*)
                        FROM "TreeRanks" tr
                        JOIN "ResearchTreeVersions" rtv ON rtv."Id" = tr."ResearchTreeVersionId"
                        WHERE rtv."Id" = e."ResearchTreeVersionId"
                          AND tr."RankNumber" = r."RankNumber"
                          AND rtv."ResearchTreeId" = rt."Id"
                    ) <> 1;

                    IF ambiguousCount > 0 THEN
                        RAISE EXCEPTION
                            'IntroduceTreeRank: % VehicleTreeEntr(y/ies) could not be mapped to exactly one TreeRank within their own version. Ensure every entry''s rank belongs to the same research tree as its version before applying this migration.',
                            ambiguousCount;
                    END IF;
                END $$;
                """);

            // 4. Drop the old FK/index before remapping: the entries will temporarily
            //    point at TreeRanks rows that do not exist in Ranks.
            migrationBuilder.DropForeignKey(
                name: "FK_VehicleTreeEntries_Ranks_RankId",
                table: "VehicleTreeEntries");

            migrationBuilder.DropIndex(
                name: "IX_VehicleTreeEntries_RankId",
                table: "VehicleTreeEntries");

            migrationBuilder.Sql("""
                UPDATE "VehicleTreeEntries" e
                SET "RankId" = (
                    SELECT tr."Id"
                    FROM "TreeRanks" tr
                    WHERE tr."ResearchTreeVersionId" = e."ResearchTreeVersionId"
                      AND tr."RankNumber" = r."RankNumber"
                )
                FROM "Ranks" r
                WHERE r."Id" = e."RankId"
                """);

            // 5. Rename the column now that all data points at TreeRanks, add
            //    the new FK, and drop the old Rank model entirely.
            migrationBuilder.RenameColumn(
                name: "RankId",
                table: "VehicleTreeEntries",
                newName: "TreeRankId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTreeEntries_TreeRankId",
                table: "VehicleTreeEntries",
                column: "TreeRankId");

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleTreeEntries_TreeRanks_TreeRankId",
                table: "VehicleTreeEntries",
                column: "TreeRankId",
                principalTable: "TreeRanks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropTable(
                name: "Ranks");
        }        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse transition: collapse per-version TreeRanks back into one
            // shared Rank row per research tree (picking one version per tree
            // deterministically), remap entries, then drop TreeRanks.
            migrationBuilder.DropForeignKey(
                name: "FK_VehicleTreeEntries_TreeRanks_TreeRankId",
                table: "VehicleTreeEntries");

            migrationBuilder.DropIndex(
                name: "IX_VehicleTreeEntries_TreeRankId",
                table: "VehicleTreeEntries");

            migrationBuilder.RenameColumn(
                name: "TreeRankId",
                table: "VehicleTreeEntries",
                newName: "RankId");

            migrationBuilder.RenameIndex(
                name: "IX_VehicleTreeEntries_TreeRankId",
                table: "VehicleTreeEntries",
                newName: "IX_VehicleTreeEntries_RankId");

            migrationBuilder.CreateTable(
                name: "Ranks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResearchTreeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RankNumber = table.Column<int>(type: "integer", nullable: false),
                    RequiredVehiclesUnlocked = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ranks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ranks_ResearchTrees_ResearchTreeId",
                        column: x => x.ResearchTreeId,
                        principalTable: "ResearchTrees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ranks_ResearchTreeId",
                table: "Ranks",
                column: "ResearchTreeId");

            // Collapse: one Rank per (tree, rank number); DISTINCT ON picks the
            // row from the version with the lowest Id so the choice is stable.
            migrationBuilder.Sql("""
                INSERT INTO "Ranks"
                    ("Id", "ResearchTreeId", "RankNumber", "RequiredVehiclesUnlocked")
                SELECT DISTINCT ON (rt."Id", tr."RankNumber")
                    gen_random_uuid(), rt."Id", tr."RankNumber", tr."RequiredVehiclesUnlocked"
                FROM "TreeRanks" tr
                JOIN "ResearchTreeVersions" rtv ON rtv."Id" = tr."ResearchTreeVersionId"
                JOIN "ResearchTrees" rt ON rt."Id" = rtv."ResearchTreeId"
                ORDER BY rt."Id", tr."RankNumber", rtv."Id"
                """);

            // 4. Drop the old FK/index before remapping: the entries will temporarily
            //    point at TreeRanks rows that do not exist in Ranks.
            migrationBuilder.DropForeignKey(
                name: "FK_VehicleTreeEntries_Ranks_RankId",
                table: "VehicleTreeEntries");

            migrationBuilder.DropIndex(
                name: "IX_VehicleTreeEntries_RankId",
                table: "VehicleTreeEntries");

            migrationBuilder.Sql("""
                UPDATE "VehicleTreeEntries" e
                SET "RankId" = (
                    SELECT r."Id"
                    FROM "Ranks" r
                    JOIN "ResearchTrees" rt ON rt."Id" = r."ResearchTreeId"
                    JOIN "ResearchTreeVersions" rtv ON rtv."ResearchTreeId" = rt."Id"
                    WHERE rtv."Id" = e."ResearchTreeVersionId"
                      AND r."RankNumber" = tr."RankNumber"
                )
                FROM "TreeRanks" tr
                WHERE tr."Id" = e."RankId"
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleTreeEntries_Ranks_RankId",
                table: "VehicleTreeEntries",
                column: "RankId",
                principalTable: "Ranks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropTable(
                name: "TreeRanks");
        }    }
}
