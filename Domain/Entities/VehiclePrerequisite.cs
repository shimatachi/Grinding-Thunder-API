namespace GrindingThunder.Api.Domain.Entities;

public class VehiclePrerequisite
{
    public Guid VehicleId { get; set; }
    public Guid PrerequisiteVehicleId { get; set; }

    public Vehicle Vehicle { get; set; } = null!;
    public Vehicle PrerequisiteVehicle { get; set; } = null!;
}