namespace GrindingThunder.Api.Domain.Entities;

/// <summary>Ownership references stable vehicle identity, never a version-specific entry.</summary>
public class PlayerUnlockedVehicle
{
    public Guid PlayerAccountId { get; set; }
    public PlayerAccount PlayerAccount { get; set; } = null!;
    public Guid VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;
}
