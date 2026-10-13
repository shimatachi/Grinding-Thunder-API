using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrindingThunder.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NormalizedUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OptimizationMode = table.Column<int>(type: "integer", nullable: false),
                    AverageRpPerMatch = table.Column<int>(type: "integer", nullable: true),
                    AverageNetSlPerMatch = table.Column<int>(type: "integer", nullable: true),
                    ProgressStamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerAccounts", x => x.Id);
                    table.CheckConstraint("CK_PlayerAccounts_AverageNetSlPerMatch", "\"AverageNetSlPerMatch\" IS NULL OR \"AverageNetSlPerMatch\" > 0");
                    table.CheckConstraint("CK_PlayerAccounts_AverageRpPerMatch", "\"AverageRpPerMatch\" IS NULL OR \"AverageRpPerMatch\" > 0");
                    table.CheckConstraint("CK_PlayerAccounts_OptimizationMode", "\"OptimizationMode\" IN (0, 1, 2, 3)");
                });

            migrationBuilder.CreateTable(
                name: "PlayerUnlockedVehicles",
                columns: table => new
                {
                    PlayerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerUnlockedVehicles", x => new { x.PlayerAccountId, x.VehicleId });
                    table.ForeignKey(
                        name: "FK_PlayerUnlockedVehicles_PlayerAccounts_PlayerAccountId",
                        column: x => x.PlayerAccountId,
                        principalTable: "PlayerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerUnlockedVehicles_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAccounts_NormalizedEmail",
                table: "PlayerAccounts",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAccounts_NormalizedUsername",
                table: "PlayerAccounts",
                column: "NormalizedUsername",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerUnlockedVehicles_VehicleId",
                table: "PlayerUnlockedVehicles",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerUnlockedVehicles");

            migrationBuilder.DropTable(
                name: "PlayerAccounts");
        }
    }
}
