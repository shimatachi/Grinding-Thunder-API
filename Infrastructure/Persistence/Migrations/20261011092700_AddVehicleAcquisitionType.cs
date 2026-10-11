using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrindingThunder.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleAcquisitionType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AcquisitionType",
                table: "VehicleTreeEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcquisitionType",
                table: "VehicleTreeEntries");
        }
    }
}
