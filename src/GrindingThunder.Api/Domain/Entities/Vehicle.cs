namespace GrindingThunder.Api.Domain.Entities;

public class Vehicle
{
    public Guid Id { get; set; }
    public Guid ResearchTreeId { get; set; }
    public ResearchTree ResearchTree { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? WikiUrl { get; set; }
    public ICollection<VehicleTreeEntry> TreeEntries { get; set; } = new List<VehicleTreeEntry>();
}
