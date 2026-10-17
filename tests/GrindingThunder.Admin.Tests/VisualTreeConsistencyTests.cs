using System.Diagnostics;
using GrindingThunder.Admin.Models;
using Xunit;

namespace GrindingThunder.Admin.Tests;

public class VisualTreeConsistencyTests
{
    [Fact]
    public void FromPublic_SparseRowIndices_NormalizesRowOffsetsPerRank()
    {
        var versionId = Guid.NewGuid();
        var rankOneId = Guid.NewGuid();
        var rankTwoId = Guid.NewGuid();

        var ranks = new[]
        {
            new TreeRankSummaryDto(rankOneId, 1, 0),
            new TreeRankSummaryDto(rankTwoId, 2, 4)
        };

        var vehicle1 = Guid.NewGuid();
        var vehicle2 = Guid.NewGuid();
        var vehicle3 = Guid.NewGuid();
        var vehicle4 = Guid.NewGuid();

        // Rank 1 rows: 10, 12
        // Rank 2 rows: 20, 25
        var entries = new[]
        {
            new PublicVehicleTreeEntryDto(vehicle1, rankOneId, versionId, "Tank 1", null, 1000, 2000, VehicleAcquisitionType.Researchable, false, null, 1, 10, []),
            new PublicVehicleTreeEntryDto(vehicle2, rankOneId, versionId, "Tank 2", null, 2000, 4000, VehicleAcquisitionType.Researchable, false, null, 1, 12, [vehicle1]),
            new PublicVehicleTreeEntryDto(vehicle3, rankTwoId, versionId, "Tank 3", null, 5000, 10000, VehicleAcquisitionType.Researchable, false, null, 1, 20, [vehicle2]),
            new PublicVehicleTreeEntryDto(vehicle4, rankTwoId, versionId, "Tank 4", null, 8000, 16000, VehicleAcquisitionType.Researchable, false, null, 2, 25, [])
        };

        var tree = VisualTreeMapper.FromPublic(ranks, entries);

        var rankOneVm = tree.Ranks.Single(r => r.Id == rankOneId);
        Assert.Equal(10, rankOneVm.MinimumTreeRow);
        Assert.Equal(12, rankOneVm.MaximumTreeRow);

        var rankTwoVm = tree.Ranks.Single(r => r.Id == rankTwoId);
        Assert.Equal(20, rankTwoVm.MinimumTreeRow);
        Assert.Equal(25, rankTwoVm.MaximumTreeRow);

        // Verify row offsets are normalized relative to rank minimum
        var node1 = tree.Nodes.Single(n => n.Id == vehicle1);
        var node2 = tree.Nodes.Single(n => n.Id == vehicle2);
        var node3 = tree.Nodes.Single(n => n.Id == vehicle3);
        var node4 = tree.Nodes.Single(n => n.Id == vehicle4);

        Assert.Equal(0, node1.RowOffset); // 10 - 10
        Assert.Equal(2, node2.RowOffset); // 12 - 10
        Assert.Equal(0, node3.RowOffset); // 20 - 20
        Assert.Equal(5, node4.RowOffset); // 25 - 20
    }

    [Fact]
    public void FromPublic_FolderParentResolution_ResolvesBothExplicitAndInferredFolderParents()
    {
        var versionId = Guid.NewGuid();
        var rankId = Guid.NewGuid();

        var ranks = new[] { new TreeRankSummaryDto(rankId, 1, 0) };

        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var entries = new[]
        {
            // Note: IsFolderParent is false in the raw DTO, but child specifies FolderParentId = parentId
            new PublicVehicleTreeEntryDto(parentId, rankId, versionId, "Parent Tank", null, 1000, 2000, VehicleAcquisitionType.Researchable, false, null, 1, 1, []),
            new PublicVehicleTreeEntryDto(childId, rankId, versionId, "Child Tank", null, 1000, 2000, VehicleAcquisitionType.Researchable, false, parentId, 1, 1, [])
        };

        var tree = VisualTreeMapper.FromPublic(ranks, entries);

        var parentNode = tree.Nodes.Single(n => n.Id == parentId);
        var childNode = tree.Nodes.Single(n => n.Id == childId);

        Assert.True(parentNode.IsFolderParent);
        Assert.False(childNode.IsFolderParent);
        Assert.Equal(parentId, childNode.FolderParentId);

        var folderEdge = Assert.Single(tree.Edges, e => e.Kind == VisualTreeEdgeKind.Folder);
        Assert.Equal(parentId, folderEdge.FromNodeId);
        Assert.Equal(childId, folderEdge.ToNodeId);
    }

    [Fact]
    public void FromPublic_And_FromDraft_YieldEquivalentNodeCoordinatesAndEdges()
    {
        var versionId = Guid.NewGuid();
        var researchTreeId = Guid.NewGuid();
        var gameUpdateId = Guid.NewGuid();
        var rankOneId = Guid.NewGuid();
        var rankTwoId = Guid.NewGuid();

        var vehicle1Id = Guid.NewGuid();
        var vehicle2Id = Guid.NewGuid();
        var vehicle3Id = Guid.NewGuid();

        // Setup Draft representation
        var draftRanks = new List<TreeRankDto>
        {
            new(rankOneId, versionId, 1, 0),
            new(rankTwoId, versionId, 2, 5)
        };
        var draftEntries = new List<VehicleTreeEntryDto>
        {
            new(vehicle1Id, versionId, vehicle1Id, rankOneId, 2900, 700, 1, 1, VehicleAcquisitionType.Researchable, null, "Vehicle 1"),
            new(vehicle2Id, versionId, vehicle2Id, rankOneId, 4000, 1000, 1, 1, VehicleAcquisitionType.Researchable, vehicle1Id, "Vehicle 2"),
            new(vehicle3Id, versionId, vehicle3Id, rankTwoId, 10000, 20000, 2, 2, VehicleAcquisitionType.Premium, null, "Vehicle 3")
        };
        var draftPrereqs = new List<VehiclePrerequisiteDto>
        {
            new(vehicle3Id, vehicle1Id, versionId)
        };
        var draft = new DraftVersionDetailDto(versionId, researchTreeId, gameUpdateId, "Draft", draftRanks, draftEntries, draftPrereqs);

        var vehicleCatalog = new List<VehicleDto>
        {
            new(vehicle1Id, "Vehicle 1", null, researchTreeId, Guid.NewGuid(), "USA", Guid.NewGuid(), "Ground", null),
            new(vehicle2Id, "Vehicle 2", null, researchTreeId, Guid.NewGuid(), "USA", Guid.NewGuid(), "Ground", null),
            new(vehicle3Id, "Vehicle 3", null, researchTreeId, Guid.NewGuid(), "USA", Guid.NewGuid(), "Ground", null)
        };

        // Setup Public representation with equivalent data
        var publicRanks = new List<TreeRankSummaryDto>
        {
            new(rankOneId, 1, 0),
            new(rankTwoId, 2, 5)
        };
        var publicEntries = new List<PublicVehicleTreeEntryDto>
        {
            new(vehicle1Id, rankOneId, versionId, "Vehicle 1", null, 2900, 700, VehicleAcquisitionType.Researchable, true, null, 1, 1, []),
            new(vehicle2Id, rankOneId, versionId, "Vehicle 2", null, 4000, 1000, VehicleAcquisitionType.Researchable, false, vehicle1Id, 1, 1, []),
            new(vehicle3Id, rankTwoId, versionId, "Vehicle 3", null, 10000, 20000, VehicleAcquisitionType.Premium, false, null, 2, 2, [vehicle1Id])
        };

        var draftTree = VisualTreeMapper.FromDraft(draft, vehicleCatalog);
        var publicTree = VisualTreeMapper.FromPublic(publicRanks, publicEntries);

        Assert.Equal(draftTree.MaximumTreeColumn, publicTree.MaximumTreeColumn);
        Assert.Equal(draftTree.Ranks.Count, publicTree.Ranks.Count);
        Assert.Equal(draftTree.Nodes.Count, publicTree.Nodes.Count);
        Assert.Equal(draftTree.Edges.Count, publicTree.Edges.Count);

        for (int i = 0; i < draftTree.Nodes.Count; i++)
        {
            var draftNode = draftTree.Nodes.Single(n => n.Id == publicTree.Nodes[i].Id);
            var publicNode = publicTree.Nodes[i];

            Assert.Equal(draftNode.TreeColumn, publicNode.TreeColumn);
            Assert.Equal(draftNode.TreeRow, publicNode.TreeRow);
            Assert.Equal(draftNode.RowOffset, publicNode.RowOffset);
            Assert.Equal(draftNode.CoordinateStackIndex, publicNode.CoordinateStackIndex);
            Assert.Equal(draftNode.AcquisitionType, publicNode.AcquisitionType);
            Assert.Equal(draftNode.IsFolderParent, publicNode.IsFolderParent);
            Assert.Equal(draftNode.FolderParentId, publicNode.FolderParentId);
        }

        foreach (var draftEdge in draftTree.Edges)
        {
            Assert.Contains(publicTree.Edges, pe =>
                pe.FromNodeId == draftEdge.FromNodeId &&
                pe.ToNodeId == draftEdge.ToNodeId &&
                pe.Kind == draftEdge.Kind);
        }
    }

    [Fact]
    public void LargeTree_MappingPerformance_CompletesUnderBudget()
    {
        var versionId = Guid.NewGuid();
        var ranks = new List<TreeRankSummaryDto>();
        var entries = new List<PublicVehicleTreeEntryDto>();

        const int rankCount = 8;

        Guid? previousRankLeaderId = null;

        for (int rankNum = 1; rankNum <= rankCount; rankNum++)
        {
            var rankId = Guid.NewGuid();
            ranks.Add(new TreeRankSummaryDto(rankId, rankNum, (rankNum - 1) * 5));

            Guid? lastInColumn = null;
            for (int col = 1; col <= 5; col++)
            {
                for (int row = 1; row <= 5; row++)
                {
                    var vehicleId = Guid.NewGuid();
                    var prereqs = new List<Guid>();

                    if (row > 1 && lastInColumn.HasValue)
                    {
                        prereqs.Add(lastInColumn.Value);
                    }
                    else if (row == 1 && previousRankLeaderId.HasValue && col == 1)
                    {
                        prereqs.Add(previousRankLeaderId.Value);
                    }

                    bool isFolderChild = row == 3 && col == 2;
                    Guid? folderParentId = isFolderChild && lastInColumn.HasValue ? lastInColumn.Value : null;

                    entries.Add(new PublicVehicleTreeEntryDto(
                        vehicleId,
                        rankId,
                        versionId,
                        $"Vehicle R{rankNum} C{col} R{row}",
                        $"https://example.test/v_{rankNum}_{col}_{row}.png",
                        rankNum * 10000 + row * 1000,
                        rankNum * 30000 + row * 2000,
                        col == 5 ? VehicleAcquisitionType.Premium : VehicleAcquisitionType.Researchable,
                        false,
                        folderParentId,
                        col,
                        (rankNum * 10) + row,
                        prereqs,
                        $"https://wiki.warthunder.com/Vehicle_{rankNum}_{col}_{row}"));

                    lastInColumn = vehicleId;
                    if (row == 5 && col == 1)
                    {
                        previousRankLeaderId = vehicleId;
                    }
                }
            }
        }

        Assert.Equal(200, entries.Count);

        // Profile the mapping execution time
        var stopwatch = Stopwatch.StartNew();
        var tree = VisualTreeMapper.FromPublic(ranks, entries);
        stopwatch.Stop();

        // 200 nodes should easily map in under 50ms (typically < 10ms in release/debug)
        Assert.True(stopwatch.ElapsedMilliseconds < 100, $"Mapping 200 nodes took {stopwatch.ElapsedMilliseconds}ms, exceeding budget.");
        Assert.Equal(200, tree.Nodes.Count);
        Assert.Equal(8, tree.Ranks.Count);
        Assert.Equal(5, tree.MaximumTreeColumn);
        Assert.NotEmpty(tree.Edges);
    }

    [Fact]
    public void FolderPresentationState_PreservesCanonicalCoordinatesForParentAndChildren()
    {
        var versionId = Guid.NewGuid();
        var rankId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var child1Id = Guid.NewGuid();
        var child2Id = Guid.NewGuid();

        var ranks = new[] { new TreeRankSummaryDto(rankId, 5, 3) };
        var entries = new[]
        {
            // Parent at Column 2, Row 3
            new PublicVehicleTreeEntryDto(parentId, rankId, versionId, "Vautour IIA", "https://example.test/vautour2a.png", 100_000, 250_000, VehicleAcquisitionType.Researchable, false, null, 2, 3, []),
            // Child 1 sharing cell (Column 2, Row 3)
            new PublicVehicleTreeEntryDto(child1Id, rankId, versionId, "Vautour IIN", "https://example.test/vautour2n.png", 110_000, 270_000, VehicleAcquisitionType.Researchable, false, parentId, 2, 3, []),
            // Child 2 placed at Column 2, Row 4
            new PublicVehicleTreeEntryDto(child2Id, rankId, versionId, "Vautour IIN(C)", "https://example.test/vautour2nc.png", 120_000, 300_000, VehicleAcquisitionType.Premium, false, parentId, 2, 4, [])
        };

        var tree = VisualTreeMapper.FromPublic(ranks, entries);

        var parentNode = Assert.Single(tree.Nodes, n => n.Id == parentId);
        var child1Node = Assert.Single(tree.Nodes, n => n.Id == child1Id);
        var child2Node = Assert.Single(tree.Nodes, n => n.Id == child2Id);

        // Canonical coordinates must remain EXACTLY as authored
        Assert.Equal(2, parentNode.TreeColumn);
        Assert.Equal(3, parentNode.TreeRow);
        Assert.True(parentNode.IsFolderParent);
        Assert.Null(parentNode.FolderParentId);

        Assert.Equal(2, child1Node.TreeColumn);
        Assert.Equal(3, child1Node.TreeRow);
        Assert.False(child1Node.IsFolderParent);
        Assert.Equal(parentId, child1Node.FolderParentId);

        Assert.Equal(2, child2Node.TreeColumn);
        Assert.Equal(4, child2Node.TreeRow);
        Assert.False(child2Node.IsFolderParent);
        Assert.Equal(parentId, child2Node.FolderParentId);

        // Stack disambiguation indexes without mutating canonical row/col
        Assert.Equal(0, parentNode.CoordinateStackIndex);
        Assert.Equal(1, child1Node.CoordinateStackIndex);
        Assert.Equal(0, child2Node.CoordinateStackIndex);

        // Folder edges must connect parent to each child
        var folderEdges = tree.Edges.Where(e => e.Kind == VisualTreeEdgeKind.Folder).ToList();
        Assert.Equal(2, folderEdges.Count);
        Assert.Contains(folderEdges, e => e.FromNodeId == parentId && e.ToNodeId == child1Id);
        Assert.Contains(folderEdges, e => e.FromNodeId == parentId && e.ToNodeId == child2Id);

        // Folder relationships must NOT create synthetic prerequisite edges
        var prerequisiteEdges = tree.Edges.Where(e => e.Kind == VisualTreeEdgeKind.Prerequisite).ToList();
        Assert.Empty(prerequisiteEdges);
    }

    [Fact]
    public void Draft_FolderPresentation_NeverMutatesPersistedCoordinatesOrPrerequisites()
    {
        var versionId = Guid.NewGuid();
        var rankId = Guid.NewGuid();
        var parentEntryId = Guid.NewGuid();
        var childEntryId = Guid.NewGuid();
        var parentVehicleId = Guid.NewGuid();
        var childVehicleId = Guid.NewGuid();

        var draft = new DraftVersionDetailDto(
            versionId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Draft",
            [new TreeRankDto(rankId, versionId, 3, 2)],
            [
                new VehicleTreeEntryDto(parentEntryId, versionId, parentVehicleId, rankId, 45_000, 120_000, 1, 2, VehicleAcquisitionType.Researchable, null, "Parent Tank"),
                new VehicleTreeEntryDto(childEntryId, versionId, childVehicleId, rankId, 48_000, 130_000, 1, 2, VehicleAcquisitionType.Researchable, parentEntryId, "Child Tank")
            ],
            []);

        var vehicles = new[]
        {
            new VehicleDto(parentVehicleId, "Parent Tank", null, Guid.NewGuid(), Guid.NewGuid(), "Nation", Guid.NewGuid(), "Army", null),
            new VehicleDto(childVehicleId, "Child Tank", null, Guid.NewGuid(), Guid.NewGuid(), "Nation", Guid.NewGuid(), "Army", null)
        };

        var tree = VisualTreeMapper.FromDraft(draft, vehicles);

        var parent = tree.Nodes.Single(n => n.Id == parentEntryId);
        var child = tree.Nodes.Single(n => n.Id == childEntryId);

        // Verifying coordinates remain unchanged
        Assert.Equal((1, 2), (parent.TreeColumn, parent.TreeRow));
        Assert.Equal((1, 2), (child.TreeColumn, child.TreeRow));
        Assert.True(parent.IsFolderParent);
        Assert.Equal(parentEntryId, child.FolderParentId);

        // Ensure edge kinds are correctly separated
        Assert.DoesNotContain(tree.Edges, e => e.Kind == VisualTreeEdgeKind.Prerequisite);
        var folderEdge = Assert.Single(tree.Edges, e => e.Kind == VisualTreeEdgeKind.Folder);
        Assert.Equal(parentEntryId, folderEdge.FromNodeId);
        Assert.Equal(childEntryId, folderEdge.ToNodeId);
    }
}
