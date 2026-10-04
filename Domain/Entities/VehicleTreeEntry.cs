namespace GrindingThunder.Api.Domain.Entities;

/// <summary>
/// A vehicle's placement and state inside ONE specific research-tree version.
/// RP cost, SL cost, and rank association are version-specific: the same stable
/// Vehicle may appear in many versions with different values. The referenced
/// TreeRank must belong to the same ResearchTreeVersion as this entry (enforced
/// by seed/application logic and tests; the schema cannot express it simply).
/// </summary>
public class VehicleTreeEntry
{
    public Guid Id { get; set; }
    public Guid ResearchTreeVersionId { get; set; }
    public Guid VehicleId { get; set; }
    public Guid TreeRankId { get; set; }
    public int RpCost { get; set; }
    public int SlCost { get; set; }

    public ResearchTreeVersion ResearchTreeVersion { get; set; } = null!;
    public Vehicle Vehicle { get; set; } = null!;
    public TreeRank TreeRank { get; set; } = null!;
}