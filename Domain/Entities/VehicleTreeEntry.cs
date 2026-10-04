namespace GrindingThunder.Api.Domain.Entities;

/// <summary>
/// A vehicle's placement and state inside ONE specific research-tree version.
/// Costs, rank, visual layout, and folder membership are version-specific: the
/// same stable Vehicle may appear in many versions with different values.
/// </summary>
public class VehicleTreeEntry
{
    public Guid Id { get; set; }
    public Guid ResearchTreeVersionId { get; set; }
    public Guid VehicleId { get; set; }
    public Guid TreeRankId { get; set; }
    public int RpCost { get; set; }
    public int SlCost { get; set; }
    public int TreeColumn { get; set; }
    public int TreeRow { get; set; }
    public Guid? FolderParentEntryId { get; set; }

    public ResearchTreeVersion ResearchTreeVersion { get; set; } = null!;
    public Vehicle Vehicle { get; set; } = null!;
    public TreeRank TreeRank { get; set; } = null!;
    public VehicleTreeEntry? FolderParentEntry { get; set; }
    public ICollection<VehicleTreeEntry> FolderChildren { get; set; } = new List<VehicleTreeEntry>();

    // Version-specific prerequisite edges (Batch 5); both ends always share
    // this entry's ResearchTreeVersion, enforced by composite foreign keys.
    public ICollection<VehiclePrerequisite> Prerequisites { get; set; } = new List<VehiclePrerequisite>();
    public ICollection<VehiclePrerequisite> RequiredFor { get; set; } = new List<VehiclePrerequisite>();
}
