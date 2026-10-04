using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrindingThunder.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleTypeAndResearchTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Ranks_Nations_NationId",
                table: "Ranks");

            migrationBuilder.CreateTable(
                name: "VehicleTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResearchTrees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NationId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleTypeId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchTrees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResearchTrees_Nations_NationId",
                        column: x => x.NationId,
                        principalTable: "Nations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ResearchTrees_VehicleTypes_VehicleTypeId",
                        column: x => x.VehicleTypeId,
                        principalTable: "VehicleTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "VehicleTypes",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), "Ground" },
                    { new Guid("10000000-0000-0000-0000-000000000002"), "Aviation" },
                    { new Guid("10000000-0000-0000-0000-000000000003"), "Helicopter" },
                    { new Guid("10000000-0000-0000-0000-000000000004"), "Coastal Fleet" },
                    { new Guid("10000000-0000-0000-0000-000000000005"), "Bluewater Fleet" }
                });

            migrationBuilder.Sql(
                """
                INSERT INTO "VehicleTypes" ("Id", "Name")
                SELECT md5('vehicle-type:' || n."Type")::uuid, n."Type"
                FROM "Nations" AS n
                WHERE n."Type" <> ''
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "VehicleTypes" AS vt
                      WHERE lower(vt."Name") = lower(n."Type"))
                GROUP BY n."Type";

                INSERT INTO "ResearchTrees" ("Id", "NationId", "VehicleTypeId")
                SELECT
                    md5('research-tree:' || n."Id"::text)::uuid,
                    n."Id",
                    COALESCE(
                        (
                            SELECT vt."Id"
                            FROM "VehicleTypes" AS vt
                            WHERE lower(vt."Name") = lower(n."Type")
                            LIMIT 1
                        ),
                        '10000000-0000-0000-0000-000000000001'::uuid)
                FROM "Nations" AS n
                ;
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "ResearchTreeId",
                table: "Ranks",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "Ranks" AS r
                SET "ResearchTreeId" = rt."Id"
                FROM "ResearchTrees" AS rt
                WHERE rt."NationId" = r."NationId";
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "ResearchTreeId",
                table: "Ranks",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "IX_Ranks_NationId",
                table: "Ranks");

            migrationBuilder.DropColumn(
                name: "NationId",
                table: "Ranks");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Nations");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchTrees_NationId_VehicleTypeId",
                table: "ResearchTrees",
                columns: new[] { "NationId", "VehicleTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResearchTrees_VehicleTypeId",
                table: "ResearchTrees",
                column: "VehicleTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Ranks_ResearchTreeId",
                table: "Ranks",
                column: "ResearchTreeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Ranks_ResearchTrees_ResearchTreeId",
                table: "Ranks",
                column: "ResearchTreeId",
                principalTable: "ResearchTrees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Ranks_ResearchTrees_ResearchTreeId",
                table: "Ranks");

            migrationBuilder.AddColumn<Guid>(
                name: "NationId",
                table: "Ranks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Nations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "Ranks" AS r
                SET "NationId" = rt."NationId"
                FROM "ResearchTrees" AS rt
                WHERE rt."Id" = r."ResearchTreeId";

                UPDATE "Nations" AS n
                SET "Type" = COALESCE(
                    (
                        SELECT vt."Name"
                        FROM "ResearchTrees" AS rt
                        INNER JOIN "VehicleTypes" AS vt ON vt."Id" = rt."VehicleTypeId"
                        WHERE rt."NationId" = n."Id"
                        ORDER BY CASE WHEN vt."Name" = 'Ground' THEN 0 ELSE 1 END, vt."Name"
                        LIMIT 1
                    ),
                    'Ground');
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "NationId",
                table: "Ranks",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "Nations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "IX_Ranks_ResearchTreeId",
                table: "Ranks");

            migrationBuilder.DropColumn(
                name: "ResearchTreeId",
                table: "Ranks");

            migrationBuilder.DropTable(
                name: "ResearchTrees");

            migrationBuilder.DropTable(
                name: "VehicleTypes");

            migrationBuilder.CreateIndex(
                name: "IX_Ranks_NationId",
                table: "Ranks",
                column: "NationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Ranks_Nations_NationId",
                table: "Ranks",
                column: "NationId",
                principalTable: "Nations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
