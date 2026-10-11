namespace GrindingThunder.Api.Domain.Entities;

/// <summary>
/// Version-specific prerequisite edge between two VehicleTreeEntries.
/// Both ends always belong to the same ResearchTreeVersion; the database
/// enforces this via composite foreign keys. Stable Vehicle identities are
/// never referenced directly - prerequisites change between game updates.
/// </summary>
public class VehiclePrerequisite
{
    public Guid VehicleTreeEntryId { get; set; }
    public Guid PrerequisiteVehicleTreeEntryId { get; set; }

    /// <summary>Denormalized version ownership; both composite FKs carry it,
    /// so a cross-version edge can never satisfy the foreign keys.</summary>
    public Guid ResearchTreeVersionId { get; set; }

    public VehicleTreeEntry VehicleTreeEntry { get; set; } = null!;
    public VehicleTreeEntry PrerequisiteVehicleTreeEntry { get; set; } = null!;
}