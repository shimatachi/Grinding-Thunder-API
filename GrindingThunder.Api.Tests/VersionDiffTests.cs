using GrindingThunder.Api.Application.Exceptions;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using GrindingThunder.Api.Infrastructure.Services;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class VersionDiffTests
{
    [Fact]
    public async Task CompareVersionsAsync_AddedRemovedAndModifiedVehicles_ReportedCorrectly()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var nation = database.AddNation("USA");
        var ground = database.AddVehicleType("Ground");
        var tree = database.AddResearchTree(nation, ground);

        var update1 = database.AddGameUpdate("2.41");
        var update2 = database.AddGameUpdate("2.43");

        var sourceVersion = database.AddResearchTreeVersion(tree, update1, ResearchTreeVersionStatus.Draft);
        var targetVersion = database.AddResearchTreeVersion(tree, update2, ResearchTreeVersionStatus.Draft);

        var sourceRank1 = database.AddTreeRank(sourceVersion, 1, 2);
        var targetRank1 = database.AddTreeRank(targetVersion, 1, 3); // modified threshold

        var vCommon = new Vehicle { Id = Guid.NewGuid(), Name = "Common Tank" };
        var vRemoved = new Vehicle { Id = Guid.NewGuid(), Name = "Removed Tank" };
        var vAdded = new Vehicle { Id = Guid.NewGuid(), Name = "Added Tank" };
        database.Context.Vehicles.AddRange(vCommon, vRemoved, vAdded);

        // Source entries
        database.AddVehicleTreeEntry(sourceVersion, vCommon, sourceRank1, 1000, 500, treeColumn: 1, treeRow: 1, acquisitionType: VehicleAcquisitionType.Researchable);
        database.AddVehicleTreeEntry(sourceVersion, vRemoved, sourceRank1, 2000, 1000, treeColumn: 2, treeRow: 1, acquisitionType: VehicleAcquisitionType.Researchable);
        database.AddPrerequisite(vRemoved, vCommon, sourceVersion);

        // Target entries: vCommon has modified RP, SL, Row, and AcquisitionType
        database.AddVehicleTreeEntry(targetVersion, vCommon, targetRank1, 1200, 600, treeColumn: 1, treeRow: 2, acquisitionType: VehicleAcquisitionType.Squadron);
        database.AddVehicleTreeEntry(targetVersion, vAdded, targetRank1, 3000, 1500, treeColumn: 2, treeRow: 2, acquisitionType: VehicleAcquisitionType.Researchable);
        database.AddPrerequisite(vAdded, vCommon, targetVersion);

        await database.SaveChangesAsync();
        await lifecycleService.PublishVersionAsync(sourceVersion.Id);

        var diff = await lifecycleService.CompareVersionsAsync(sourceVersion.Id, targetVersion.Id);

        Assert.Equal(sourceVersion.Id, diff.SourceVersionId);
        Assert.Equal(targetVersion.Id, diff.TargetVersionId);
        Assert.Equal("2.41", diff.SourceGameUpdateVersion);
        Assert.Equal("2.43", diff.TargetGameUpdateVersion);

        // Added vehicles
        var added = Assert.Single(diff.AddedVehicles);
        Assert.Equal(vAdded.Id, added.VehicleId);
        Assert.Equal("Added Tank", added.Name);
        Assert.Equal(3000, added.RpCost);
        Assert.Equal(1500, added.SlCost);

        // Removed vehicles
        var removed = Assert.Single(diff.RemovedVehicles);
        Assert.Equal(vRemoved.Id, removed.VehicleId);
        Assert.Equal("Removed Tank", removed.Name);
        Assert.Equal(2000, removed.RpCost);
        Assert.Equal(1000, removed.SlCost);

        // Modified vehicles
        var modified = Assert.Single(diff.ModifiedVehicles);
        Assert.Equal(vCommon.Id, modified.VehicleId);
        Assert.Equal("Common Tank", modified.Name);
        Assert.Equal(1000, modified.OldRpCost);
        Assert.Equal(1200, modified.NewRpCost);
        Assert.Equal(200, modified.RpDelta);
        Assert.Equal(500, modified.OldSlCost);
        Assert.Equal(600, modified.NewSlCost);
        Assert.Equal(100, modified.SlDelta);
        Assert.Equal(1, modified.OldRow);
        Assert.Equal(2, modified.NewRow);
        Assert.Equal(VehicleAcquisitionType.Researchable, modified.OldAcquisitionType);
        Assert.Equal(VehicleAcquisitionType.Squadron, modified.NewAcquisitionType);

        // Prerequisites diff
        var addedPrereq = Assert.Single(diff.AddedPrerequisites);
        Assert.Equal(vAdded.Id, addedPrereq.VehicleId);
        Assert.Equal(vCommon.Id, addedPrereq.PrerequisiteVehicleId);

        var removedPrereq = Assert.Single(diff.RemovedPrerequisites);
        Assert.Equal(vRemoved.Id, removedPrereq.VehicleId);
        Assert.Equal(vCommon.Id, removedPrereq.PrerequisiteVehicleId);

        // Rank changes
        var rankChange = Assert.Single(diff.RankChanges);
        Assert.Equal(1, rankChange.RankNumber);
        Assert.Equal(2, rankChange.OldRequiredVehiclesUnlocked);
        Assert.Equal(3, rankChange.NewRequiredVehiclesUnlocked);
        Assert.Equal(RankChangeType.Modified, rankChange.ChangeType);

        // Summary
        Assert.Equal(1, diff.Summary.AddedVehiclesCount);
        Assert.Equal(1, diff.Summary.RemovedVehiclesCount);
        Assert.Equal(1, diff.Summary.ModifiedVehiclesCount);
        Assert.Equal(1, diff.Summary.AddedPrerequisitesCount);
        Assert.Equal(1, diff.Summary.RemovedPrerequisitesCount);
        // Net RP: 3000 (added) - 2000 (removed) + 200 (delta) = 1200
        Assert.Equal(1200, diff.Summary.NetRpDelta);
        // Net SL: 1500 (added) - 1000 (removed) + 100 (delta) = 600
        Assert.Equal(600, diff.Summary.NetSlDelta);
    }

    [Fact]
    public async Task CompareVersionsAsync_RankAddedAndRemoved_ReportedInRankChanges()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var nation = database.AddNation("USSR");
        var tree = database.AddResearchTree(nation, database.AddVehicleType("Aviation"));

        var update1 = database.AddGameUpdate("2.39");
        var update2 = database.AddGameUpdate("2.41");

        var sourceVersion = database.AddResearchTreeVersion(tree, update1);
        var targetVersion = database.AddResearchTreeVersion(tree, update2);

        // Source has rank 1 and 2
        database.AddTreeRank(sourceVersion, 1, 3);
        database.AddTreeRank(sourceVersion, 2, 4);

        // Target has rank 1 (same requirement) and rank 3 (new rank)
        database.AddTreeRank(targetVersion, 1, 3);
        database.AddTreeRank(targetVersion, 3, 5);

        await database.SaveChangesAsync();

        var diff = await lifecycleService.CompareVersionsAsync(sourceVersion.Id, targetVersion.Id);

        Assert.Equal(2, diff.RankChanges.Count);

        var removedRank = diff.RankChanges.Single(r => r.ChangeType == RankChangeType.Removed);
        Assert.Equal(2, removedRank.RankNumber);
        Assert.Equal(4, removedRank.OldRequiredVehiclesUnlocked);
        Assert.Null(removedRank.NewRequiredVehiclesUnlocked);

        var addedRank = diff.RankChanges.Single(r => r.ChangeType == RankChangeType.Added);
        Assert.Equal(3, addedRank.RankNumber);
        Assert.Null(addedRank.OldRequiredVehiclesUnlocked);
        Assert.Equal(5, addedRank.NewRequiredVehiclesUnlocked);
    }

    [Fact]
    public async Task CompareVersionsAsync_NonExistentVersion_ThrowsNotFound()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree, update);
        await database.SaveChangesAsync();

        var nonExistentId = Guid.NewGuid();

        await Assert.ThrowsAsync<ResearchTreeVersionNotFoundException>(
            () => lifecycleService.CompareVersionsAsync(version.Id, nonExistentId));

        await Assert.ThrowsAsync<ResearchTreeVersionNotFoundException>(
            () => lifecycleService.CompareVersionsAsync(nonExistentId, version.Id));
    }

    [Fact]
    public async Task CompareVersionsAsync_DifferentResearchTrees_ThrowsInvalidOperationException()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var nation = database.AddNation();
        var ground = database.AddVehicleType("Ground");
        var aviation = database.AddVehicleType("Aviation");

        var groundTree = database.AddResearchTree(nation, ground);
        var aviationTree = database.AddResearchTree(nation, aviation);

        var update = database.AddGameUpdate("2.43");
        var groundVersion = database.AddResearchTreeVersion(groundTree, update);
        var aviationVersion = database.AddResearchTreeVersion(aviationTree, update);
        await database.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycleService.CompareVersionsAsync(groundVersion.Id, aviationVersion.Id));

        Assert.Contains("different research trees", exception.Message);
    }
}
