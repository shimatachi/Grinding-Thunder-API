namespace GrindingThunder.Admin.Models;

/// <summary>
/// Presentation-only representation of one exact research-tree version. It keeps
/// logical tree coordinates and graph relationships independent from Razor and
/// from the API projection used to load Draft or Published data.
/// </summary>
public sealed record VisualResearchTreeViewModel(
    IReadOnlyList<VisualTreeRankViewModel> Ranks,
    IReadOnlyList<VisualTreeNodeViewModel> Nodes,
    IReadOnlyList<VisualTreeEdgeViewModel> Edges,
    int MaximumTreeColumn)
{
    public bool IsEmpty => Nodes.Count == 0;
}

/// <summary>
/// Page-specific context layered over the reusable visual tree projection.
/// Editing capabilities are supplied by the Razor Page so the tree mapper stays
/// independent from authentication and HTTP concerns.
/// </summary>
public sealed record VisualTreeWorkspaceViewModel(
    VisualResearchTreeViewModel Tree,
    bool CanEdit,
    Guid VersionId,
    string ResearchTreeLabel,
    Guid? ResearchTreeId,
    DraftVersionDetailDto? Draft,
    IReadOnlyList<VehicleDto> AvailableVehicles,
    string? ErrorMessage,
    bool StartInEdgeMode,
    bool StartWithSelectedEdge,
    Guid? PendingDependentEntryId,
    Guid? PendingPrerequisiteEntryId)
{
    public IReadOnlyList<TreeRankDto> Ranks => Draft?.TreeRanks ?? [];
    public IReadOnlyList<VehicleTreeEntryDto> Entries => Draft?.Entries ?? [];
}

public sealed record VisualEntryFieldsViewModel(
    string FieldPrefix,
    string IdPrefix,
    IReadOnlyList<TreeRankDto> Ranks,
    IReadOnlyList<VehicleTreeEntryDto> Entries);

public sealed record VisualTreeRankViewModel(
    Guid Id,
    int RankNumber,
    int RequiredVehiclesUnlocked,
    int MinimumTreeRow,
    int MaximumTreeRow,
    IReadOnlyList<VisualTreeNodeViewModel> Nodes);

public sealed record VisualTreeNodeViewModel(
    Guid Id,
    Guid VehicleId,
    Guid TreeRankId,
    int RankNumber,
    string Name,
    string? ImageUrl,
    int RpCost,
    int SlCost,
    int TreeColumn,
    int TreeRow,
    int RowOffset,
    int CoordinateStackIndex,
    VehicleAcquisitionType AcquisitionType,
    bool IsFolderParent,
    Guid? FolderParentId)
{
    public string DomId => $"tree-node-{Id:N}";
}

public enum VisualTreeEdgeKind
{
    Prerequisite,
    Folder
}

public sealed record VisualTreeEdgeViewModel(
    Guid FromNodeId,
    Guid ToNodeId,
    VisualTreeEdgeKind Kind);

/// <summary>
/// Maps the two API read shapes into the shared visual representation. The
/// mapper deliberately copies persisted rank, coordinate, folder, and graph
/// data; it never infers progression from visual proximity.
/// </summary>
public static class VisualTreeMapper
{
    public static VisualResearchTreeViewModel FromDraft(
        DraftVersionDetailDto draft,
        IReadOnlyCollection<VehicleDto> vehicles)
    {
        var ranks = draft.TreeRanks
            .Select(rank => new RankSource(rank.Id, rank.RankNumber, rank.RequiredVehiclesUnlocked))
            .ToList();
        var images = vehicles.ToDictionary(vehicle => vehicle.Id, vehicle => vehicle.ImageUrl);
        var folderParentIds = draft.Entries
            .Where(entry => entry.FolderParentEntryId.HasValue)
            .Select(entry => entry.FolderParentEntryId!.Value)
            .ToHashSet();
        var nodes = draft.Entries.Select(entry => new NodeSource(
            entry.Id,
            entry.VehicleId,
            entry.TreeRankId,
            entry.VehicleName ?? "Unknown vehicle",
            images.GetValueOrDefault(entry.VehicleId),
            entry.RpCost,
            entry.SlCost,
            entry.TreeColumn,
            entry.TreeRow,
            entry.AcquisitionType,
            folderParentIds.Contains(entry.Id),
            entry.FolderParentEntryId)).ToList();
        var prerequisiteEdges = draft.Prerequisites.Select(edge => new VisualTreeEdgeViewModel(
            edge.PrerequisiteVehicleTreeEntryId,
            edge.VehicleTreeEntryId,
            VisualTreeEdgeKind.Prerequisite));

        return Map(ranks, nodes, prerequisiteEdges);
    }

    public static VisualResearchTreeViewModel FromPublic(
        IReadOnlyCollection<TreeRankSummaryDto> ranks,
        IReadOnlyCollection<PublicVehicleTreeEntryDto> entries)
    {
        var rankSources = ranks
            .Select(rank => new RankSource(rank.Id, rank.RankNumber, rank.RequiredVehiclesUnlocked))
            .ToList();
        var folderParentIds = entries
            .Where(entry => entry.FolderParentId.HasValue)
            .Select(entry => entry.FolderParentId!.Value)
            .ToHashSet();
        var nodes = entries.Select(entry => new NodeSource(
            entry.Id,
            entry.Id,
            entry.TreeRankId,
            entry.Name,
            entry.ImageUrl,
            entry.RpCost,
            entry.SlCost,
            entry.TreeColumn,
            entry.TreeRow,
            entry.AcquisitionType,
            entry.IsFolderParent || folderParentIds.Contains(entry.Id),
            entry.FolderParentId)).ToList();
        var prerequisiteEdges = entries.SelectMany(entry => entry.PrerequisiteIds.Select(prerequisiteId =>
            new VisualTreeEdgeViewModel(prerequisiteId, entry.Id, VisualTreeEdgeKind.Prerequisite)));

        return Map(rankSources, nodes, prerequisiteEdges);
    }

    private static VisualResearchTreeViewModel Map(
        IReadOnlyCollection<RankSource> rankSources,
        IReadOnlyCollection<NodeSource> nodeSources,
        IEnumerable<VisualTreeEdgeViewModel> prerequisiteEdges)
    {
        var rankNumbers = rankSources.ToDictionary(rank => rank.Id, rank => rank.RankNumber);
        var coordinateStacks = nodeSources
            .GroupBy(node => (node.TreeRankId, node.TreeColumn, node.TreeRow))
            .SelectMany(group => group
                .OrderBy(node => node.FolderParentId.HasValue)
                .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                .Select((node, index) => (node.Id, Index: index)))
            .ToDictionary(item => item.Id, item => item.Index);

        var nodes = nodeSources
            .Where(node => rankNumbers.ContainsKey(node.TreeRankId))
            .Select(node => new VisualTreeNodeViewModel(
                node.Id,
                node.VehicleId,
                node.TreeRankId,
                rankNumbers[node.TreeRankId],
                node.Name,
                node.ImageUrl,
                node.RpCost,
                node.SlCost,
                node.TreeColumn,
                node.TreeRow,
                0,
                coordinateStacks[node.Id],
                node.AcquisitionType,
                node.IsFolderParent,
                node.FolderParentId))
            .ToList();

        var visualRanks = rankSources
            .OrderBy(rank => rank.RankNumber)
            .Select(rank =>
            {
                var rankNodes = nodes
                    .Where(node => node.TreeRankId == rank.Id)
                    .OrderBy(node => node.TreeRow)
                    .ThenBy(node => node.TreeColumn)
                    .ThenBy(node => node.CoordinateStackIndex)
                    .ToList();
                var minimumRow = rankNodes.Count == 0 ? 0 : rankNodes.Min(node => node.TreeRow);
                var maximumRow = rankNodes.Count == 0 ? 0 : rankNodes.Max(node => node.TreeRow);
                var positionedNodes = rankNodes
                    .Select(node => node with { RowOffset = node.TreeRow - minimumRow })
                    .ToList();
                return new VisualTreeRankViewModel(
                    rank.Id,
                    rank.RankNumber,
                    rank.RequiredVehiclesUnlocked,
                    minimumRow,
                    maximumRow,
                    positionedNodes);
            })
            .ToList();

        var positionedById = visualRanks.SelectMany(rank => rank.Nodes).ToDictionary(node => node.Id);
        var edges = prerequisiteEdges
            .Concat(nodes
                .Where(node => node.FolderParentId.HasValue)
                .Select(node => new VisualTreeEdgeViewModel(
                    node.FolderParentId!.Value,
                    node.Id,
                    VisualTreeEdgeKind.Folder)))
            .Where(edge => positionedById.ContainsKey(edge.FromNodeId) && positionedById.ContainsKey(edge.ToNodeId))
            .Distinct()
            .ToList();
        var maximumColumn = nodes.Count == 0 ? 0 : nodes.Max(node => node.TreeColumn);

        return new VisualResearchTreeViewModel(visualRanks, positionedById.Values.ToList(), edges, maximumColumn);
    }

    private sealed record RankSource(Guid Id, int RankNumber, int RequiredVehiclesUnlocked);

    private sealed record NodeSource(
        Guid Id,
        Guid VehicleId,
        Guid TreeRankId,
        string Name,
        string? ImageUrl,
        int RpCost,
        int SlCost,
        int TreeColumn,
        int TreeRow,
        VehicleAcquisitionType AcquisitionType,
        bool IsFolderParent,
        Guid? FolderParentId);
}
