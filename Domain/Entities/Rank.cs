namespace GrindingThunder.Api.Domain.Entities;

public class Rank
{
    public Guid Id { get; set; }
    public Guid ResearchTreeId { get; set; }
    public int RankNumber { get; set; }
    public int RequiredVehiclesUnlocked { get; set; }

    public ResearchTree ResearchTree { get; set; } = null!;
    public ICollection<VehicleTreeEntry> TreeEntries { get; set; } = new List<VehicleTreeEntry>();
}
