namespace GrindingThunder.Api.Domain.Entities;

/// <summary>
/// A snapshot of one research tree as it exists for a particular game update.
/// Belongs to exactly one ResearchTree and one GameUpdate. Later batches will
/// move update-dependent vehicle data (costs, rank, prerequisites, layout)
/// onto the version; this batch only establishes the versioning backbone.
/// </summary>
public class ResearchTreeVersion
{
    public Guid Id { get; set; }
    public Guid ResearchTreeId { get; set; }
    public Guid GameUpdateId { get; set; }

    /// <summary>Draft versions are editable; published versions become
    /// immutable historical snapshots. No publishing workflow exists yet.</summary>
    public ResearchTreeVersionStatus Status { get; set; }

    public ResearchTree ResearchTree { get; set; } = null!;
    public GameUpdate GameUpdate { get; set; } = null!;
    public ICollection<TreeRank> TreeRanks { get; set; } = new List<TreeRank>();
    public ICollection<VehicleTreeEntry> Entries { get; set; } = new List<VehicleTreeEntry>();
}

/// <summary>Lifecycle states for a research tree version.</summary>
public enum ResearchTreeVersionStatus
{
    Draft = 0,
    Published = 1
}
