using GrindingThunder.Admin.Models;
using Xunit;

namespace GrindingThunder.Admin.Tests;

public class VisualTreeMapperTests
{
    [Fact]
    public void FromDraft_MapsSparseRanksCoordinatesQuotasAndBothRelationshipKinds()
    {
        var versionId = Guid.NewGuid();
        var rankFourId = Guid.NewGuid();
        var rankSevenId = Guid.NewGuid();
        var parentEntryId = Guid.NewGuid();
        var childEntryId = Guid.NewGuid();
        var parentVehicleId = Guid.NewGuid();
        var childVehicleId = Guid.NewGuid();
        var draft = new DraftVersionDetailDto(
            versionId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Draft",
            [
                new TreeRankDto(rankSevenId, versionId, 7, 4),
                new TreeRankDto(rankFourId, versionId, 4, 2)
            ],
            [
                new VehicleTreeEntryDto(parentEntryId, versionId, parentVehicleId, rankFourId, 120_000, 300_000, 3, 8, VehicleAcquisitionType.Researchable, null, "Parent"),
                new VehicleTreeEntryDto(childEntryId, versionId, childVehicleId, rankSevenId, 240_000, 600_000, 5, 12, VehicleAcquisitionType.Premium, parentEntryId, "Child")
            ],
            [new VehiclePrerequisiteDto(childEntryId, parentEntryId, versionId)]);
        var vehicles = new[]
        {
            new VehicleDto(parentVehicleId, "Parent", "https://example.test/parent.png", Guid.NewGuid(), Guid.NewGuid(), "Nation", Guid.NewGuid(), "Aviation", "https://wiki.warthunder.com/Parent"),
            new VehicleDto(childVehicleId, "Child", null, Guid.NewGuid(), Guid.NewGuid(), "Nation", Guid.NewGuid(), "Aviation", null)
        };

        var result = VisualTreeMapper.FromDraft(draft, vehicles);

        Assert.Equal([4, 7], result.Ranks.Select(rank => rank.RankNumber));
        Assert.Equal([2, 4], result.Ranks.Select(rank => rank.RequiredVehiclesUnlocked));
        var parent = Assert.Single(result.Nodes, node => node.Id == parentEntryId);
        Assert.Equal((3, 8), (parent.TreeColumn, parent.TreeRow));
        Assert.Equal("https://example.test/parent.png", parent.ImageUrl);
        Assert.Equal("https://wiki.warthunder.com/Parent", parent.WikiUrl);
        Assert.True(parent.IsFolderParent);
        var child = Assert.Single(result.Nodes, node => node.Id == childEntryId);
        Assert.Equal((5, 12), (child.TreeColumn, child.TreeRow));
        Assert.Null(child.WikiUrl);
        Assert.Equal(VehicleAcquisitionType.Premium, child.AcquisitionType);
        Assert.Contains(new VisualTreeEdgeViewModel(parentEntryId, childEntryId, VisualTreeEdgeKind.Prerequisite), result.Edges);
        Assert.Contains(new VisualTreeEdgeViewModel(parentEntryId, childEntryId, VisualTreeEdgeKind.Folder), result.Edges);
    }

    [Fact]
    public void FromPublic_MapsPublishedSnapshotWithoutInferringEdgesFromCoordinates()
    {
        var versionId = Guid.NewGuid();
        var rankFourId = Guid.NewGuid();
        var rankSixId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var ranks = new[]
        {
            new TreeRankSummaryDto(rankFourId, 4, 3),
            new TreeRankSummaryDto(rankSixId, 6, 5)
        };
        var entries = new[]
        {
            new PublicVehicleTreeEntryDto(firstId, rankFourId, versionId, "First", null, 10, 20, VehicleAcquisitionType.Squadron, false, null, 2, 9, [], "https://wiki.warthunder.com/First"),
            new PublicVehicleTreeEntryDto(secondId, rankSixId, versionId, "Second", "https://example.test/second.png", 30, 40, VehicleAcquisitionType.Event, false, null, 2, 9, [firstId])
        };

        var result = VisualTreeMapper.FromPublic(ranks, entries);

        Assert.Equal([4, 6], result.Ranks.Select(rank => rank.RankNumber));
        Assert.Equal([3, 5], result.Ranks.Select(rank => rank.RequiredVehiclesUnlocked));
        Assert.All(result.Nodes, node => Assert.Equal((2, 9), (node.TreeColumn, node.TreeRow)));
        Assert.Equal("https://wiki.warthunder.com/First", result.Nodes.First(n => n.Id == firstId).WikiUrl);
        Assert.Null(result.Nodes.First(n => n.Id == secondId).WikiUrl);
        var edge = Assert.Single(result.Edges);
        Assert.Equal(new VisualTreeEdgeViewModel(firstId, secondId, VisualTreeEdgeKind.Prerequisite), edge);
    }
}
