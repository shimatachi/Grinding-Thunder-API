using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrindingThunder.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Nations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Ranks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RankNumber = table.Column<int>(type: "integer", nullable: false),
                    RequiredVehiclesUnlocked = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ranks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ranks_Nations_NationId",
                        column: x => x.NationId,
                        principalTable: "Nations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RankId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    RpCost = table.Column<int>(type: "integer", nullable: false),
                    SlCost = table.Column<int>(type: "integer", nullable: false),
                    IsFolderParent = table.Column<bool>(type: "boolean", nullable: false),
                    FolderParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    TreeColumn = table.Column<int>(type: "integer", nullable: false),
                    TreeRow = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vehicles_Ranks_RankId",
                        column: x => x.RankId,
                        principalTable: "Ranks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Vehicles_Vehicles_FolderParentId",
                        column: x => x.FolderParentId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VehiclePrerequisites",
                columns: table => new
                {
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrerequisiteVehicleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehiclePrerequisites", x => new { x.VehicleId, x.PrerequisiteVehicleId });
                    table.ForeignKey(
                        name: "FK_VehiclePrerequisites_Vehicles_PrerequisiteVehicleId",
                        column: x => x.PrerequisiteVehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VehiclePrerequisites_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ranks_NationId",
                table: "Ranks",
                column: "NationId");

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePrerequisites_PrerequisiteVehicleId",
                table: "VehiclePrerequisites",
                column: "PrerequisiteVehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_FolderParentId",
                table: "Vehicles",
                column: "FolderParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_RankId",
                table: "Vehicles",
                column: "RankId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VehiclePrerequisites");

            migrationBuilder.DropTable(
                name: "Vehicles");

            migrationBuilder.DropTable(
                name: "Ranks");

            migrationBuilder.DropTable(
                name: "Nations");
        }
    }
}
