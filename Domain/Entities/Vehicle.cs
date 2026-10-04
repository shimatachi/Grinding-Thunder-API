namespace GrindingThunder.Api.Domain.Entities;

public class Vehicle
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }

    // TRANSITIONAL (Batch 3): folder/layout state still stored on Vehicle.
    // It moves to versioned tree state in a later batch (see docs/DOMAIN.md).
    public bool IsFolderParent { get; set; }
    public Guid? FolderParentId { get; set; }
    public int TreeColumn { get; set; }
    public int TreeRow { get; set; }

    public Vehicle? FolderParent { get; set; }
    public ICollection<Vehicle> FolderChildren { get; set; } = new List<Vehicle>();
    public ICollection<VehicleTreeEntry> TreeEntries { get; set; } = new List<VehicleTreeEntry>();
}