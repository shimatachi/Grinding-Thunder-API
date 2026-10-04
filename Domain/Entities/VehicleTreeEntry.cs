namespace GrindingThunder.Api.Domain.Entities;

/// <summary>
/// A vehicle's placement and state inside ONE specific research-tree version.
/// RP cost, SL cost, and rank association are version-specific: the same stable
/// Vehicle may appear in many versions with different values.
/// TRANSITIONAL (Batch 3): RankId still references the non-versioned Rank model;
/// it will be replaced by a versioned TreeRank in Batch 4.
/// </summary>
public class VehicleTreeEntry
{
    public Guid Id { get; set; }
    public Guid ResearchTreeVersionId { get; set; }
    public Guid VehicleId { get; set; }
    public Guid RankId { get; set; }
    public int RpCost { get; set; }
    public int SlCost { get; set; }

    public ResearchTreeVersion ResearchTreeVersion { get; set; } = null!;
    public Vehicle Vehicle { get; set; } = null!;
    public Rank Rank { get; set; } = null!;
}