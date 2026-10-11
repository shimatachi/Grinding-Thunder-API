namespace GrindingThunder.Api.Domain.Entities;

/// <summary>
/// A rank configuration belonging to exactly one research-tree version.
/// Rank number and rank-gate requirement are version-specific: the same
/// rank number may exist in different versions with different requirements.
/// </summary>
public class TreeRank
{
    public Guid Id { get; set; }
    public Guid ResearchTreeVersionId { get; set; }
    public int RankNumber { get; set; }
    public int RequiredVehiclesUnlocked { get; set; }

    public ResearchTreeVersion ResearchTreeVersion { get; set; } = null!;
    public ICollection<VehicleTreeEntry> TreeEntries { get; set; } = new List<VehicleTreeEntry>();
}