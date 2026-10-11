namespace GrindingThunder.Api.Domain.Entities;

public class Vehicle
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public ICollection<VehicleTreeEntry> TreeEntries { get; set; } = new List<VehicleTreeEntry>();
}
