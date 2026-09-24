namespace GrindingThunder.Api.Domain.Entities;

public class Vehicle
{
    public Guid Id { get; set; }
    public Guid RankId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public int RpCost { get; set; }
    public int SlCost { get; set; }
    public bool IsFolderParent { get; set; }
    public Guid? FolderParentId { get; set; }
    public int TreeColumn { get; set; }
    public int TreeRow { get; set; }

    public Rank Rank { get; set; } = null!;
    public Vehicle? FolderParent { get; set; }
    public ICollection<Vehicle> FolderChildren { get; set; } = new List<Vehicle>();
    public ICollection<VehiclePrerequisite> Prerequisites { get; set; } = new List<VehiclePrerequisite>();
    public ICollection<VehiclePrerequisite> RequiredFor { get; set; } = new List<VehiclePrerequisite>();
}
