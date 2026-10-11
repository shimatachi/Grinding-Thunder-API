using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using GrindingThunder.Api.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class VersionValidationTests
{
    [Fact]
    public async Task ValidateVersionAsync_ValidVersion_PassesWithIsValidTrueAndEmptyErrors()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);

        var rank1 = database.AddTreeRank(version, 1, 2);
        var rank2 = database.AddTreeRank(version, 2, 2);

        var v1 = new Vehicle { Id = Guid.NewGuid(), Name = "M2 Light" };
        var v2 = new Vehicle { Id = Guid.NewGuid(), Name = "M3 Stuart" };
        var v3 = new Vehicle { Id = Guid.NewGuid(), Name = "M2A4" };
        var v4 = new Vehicle { Id = Guid.NewGuid(), Name = "M4A1 Sherman" };
        database.Context.Vehicles.AddRange(v1, v2, v3, v4);

        var e1 = database.AddVehicleTreeEntry(version, v1, rank1, 2900, 700, treeColumn: 1, treeRow: 1, acquisitionType: VehicleAcquisitionType.Researchable);
        var e2 = database.AddVehicleTreeEntry(version, v2, rank1, 4000, 1400, treeColumn: 1, treeRow: 2, acquisitionType: VehicleAcquisitionType.Researchable);
        // e3 is a folder child of e1, in same rank
        var e3 = database.AddVehicleTreeEntry(version, v3, rank1, 2900, 700, treeColumn: 1, treeRow: 1, folderParent: e1, acquisitionType: VehicleAcquisitionType.Researchable);
        var e4 = database.AddVehicleTreeEntry(version, v4, rank2, 9200, 3800, treeColumn: 1, treeRow: 3, acquisitionType: VehicleAcquisitionType.Researchable);

        database.AddPrerequisite(v2, v1, version);
        database.AddPrerequisite(v4, v2, version);
        await database.SaveChangesAsync();

        var result = await lifecycleService.ValidateVersionAsync(version.Id);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateVersionAsync_PrerequisiteCycle_IsFlaggedAsError()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);
        var rank = database.AddTreeRank(version, 1);

        var v1 = new Vehicle { Id = Guid.NewGuid(), Name = "Tank A" };
        var v2 = new Vehicle { Id = Guid.NewGuid(), Name = "Tank B" };
        database.Context.Vehicles.AddRange(v1, v2);

        database.AddVehicleTreeEntry(version, v1, rank, 1000, treeColumn: 1, treeRow: 1);
        database.AddVehicleTreeEntry(version, v2, rank, 2000, treeColumn: 2, treeRow: 1);

        // A -> B -> A cycle
        database.AddPrerequisite(v1, v2, version);
        database.AddPrerequisite(v2, v1, version);
        await database.SaveChangesAsync();

        var result = await lifecycleService.ValidateVersionAsync(version.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Prerequisite cycle detected"));
    }

    [Fact]
    public async Task ValidateVersionAsync_PrerequisitePointingToHigherRank_IsFlaggedAsError()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);
        var rank1 = database.AddTreeRank(version, 1);
        var rank2 = database.AddTreeRank(version, 2);

        var v1 = new Vehicle { Id = Guid.NewGuid(), Name = "Lower Rank Tank" };
        var v2 = new Vehicle { Id = Guid.NewGuid(), Name = "Higher Rank Tank" };
        database.Context.Vehicles.AddRange(v1, v2);

        database.AddVehicleTreeEntry(version, v1, rank1, 1000, treeColumn: 1, treeRow: 1);
        database.AddVehicleTreeEntry(version, v2, rank2, 2000, treeColumn: 1, treeRow: 2);

        // Lower rank vehicle requires higher rank vehicle as prerequisite
        database.AddPrerequisite(v1, v2, version);
        await database.SaveChangesAsync();

        var result = await lifecycleService.ValidateVersionAsync(version.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("cannot belong to a higher rank than"));
    }

    [Fact]
    public async Task ValidateVersionAsync_FolderMultiLevelParent_IsFlaggedAsError()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);
        var rank = database.AddTreeRank(version, 1);

        var v1 = new Vehicle { Id = Guid.NewGuid(), Name = "Grandparent" };
        var v2 = new Vehicle { Id = Guid.NewGuid(), Name = "Parent" };
        var v3 = new Vehicle { Id = Guid.NewGuid(), Name = "Child" };
        database.Context.Vehicles.AddRange(v1, v2, v3);

        var e1 = database.AddVehicleTreeEntry(version, v1, rank, 1000, treeColumn: 1, treeRow: 1);
        var e2 = database.AddVehicleTreeEntry(version, v2, rank, 2000, treeColumn: 1, treeRow: 1, folderParent: e1);
        var e3 = database.AddVehicleTreeEntry(version, v3, rank, 3000, treeColumn: 1, treeRow: 1, folderParent: e2);
        await database.SaveChangesAsync();

        var result = await lifecycleService.ValidateVersionAsync(version.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("cannot itself have a folder parent"));
    }

    [Fact]
    public async Task ValidateVersionAsync_FolderChildInDifferentRankThanParent_IsFlaggedAsError()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);
        var rank1 = database.AddTreeRank(version, 1);
        var rank2 = database.AddTreeRank(version, 2);

        var v1 = new Vehicle { Id = Guid.NewGuid(), Name = "Parent Rank 1" };
        var v2 = new Vehicle { Id = Guid.NewGuid(), Name = "Child Rank 2" };
        database.Context.Vehicles.AddRange(v1, v2);

        var e1 = database.AddVehicleTreeEntry(version, v1, rank1, 1000, treeColumn: 1, treeRow: 1);
        var e2 = database.AddVehicleTreeEntry(version, v2, rank2, 2000, treeColumn: 1, treeRow: 1, folderParent: e1);
        await database.SaveChangesAsync();

        var result = await lifecycleService.ValidateVersionAsync(version.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("must belong to the same rank"));
    }

    [Fact]
    public async Task ValidateVersionAsync_CoordinateCollisionBetweenRootEntries_IsFlaggedAsError()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);
        var rank = database.AddTreeRank(version, 1);

        var v1 = new Vehicle { Id = Guid.NewGuid(), Name = "Tank Alpha" };
        var v2 = new Vehicle { Id = Guid.NewGuid(), Name = "Tank Beta" };
        database.Context.Vehicles.AddRange(v1, v2);

        // Both root entries share column 1, row 1
        database.AddVehicleTreeEntry(version, v1, rank, 1000, treeColumn: 1, treeRow: 1);
        database.AddVehicleTreeEntry(version, v2, rank, 2000, treeColumn: 1, treeRow: 1);
        await database.SaveChangesAsync();

        var result = await lifecycleService.ValidateVersionAsync(version.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Multiple root vehicles share coordinates (1, 1)"));
    }

    [Fact]
    public async Task ValidateVersionAsync_UnfeasibleRankGateThreshold_IsFlaggedAsError()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);
        var rank1 = database.AddTreeRank(version, 1);
        // Rank 2 requires 5 unlocked vehicles, but Rank 1 only contains 2 vehicles
        var rank2 = database.AddTreeRank(version, 2, requiredVehiclesUnlocked: 5);

        var v1 = new Vehicle { Id = Guid.NewGuid(), Name = "Tank 1" };
        var v2 = new Vehicle { Id = Guid.NewGuid(), Name = "Tank 2" };
        database.Context.Vehicles.AddRange(v1, v2);

        database.AddVehicleTreeEntry(version, v1, rank1, 1000, treeColumn: 1, treeRow: 1);
        database.AddVehicleTreeEntry(version, v2, rank1, 2000, treeColumn: 2, treeRow: 1);
        await database.SaveChangesAsync();

        var result = await lifecycleService.ValidateVersionAsync(version.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Rank 2 requires 5 unlocked vehicles, but only 2 eligible vehicles are available."));
    }

    [Fact]
    public async Task ValidateVersionAsync_NonResearchablePrerequisiteForResearchableVehicle_IsFlaggedAsError()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);
        var rank = database.AddTreeRank(version, 1);

        var researchable = new Vehicle { Id = Guid.NewGuid(), Name = "Standard Tech Tree Tank" };
        var premium = new Vehicle { Id = Guid.NewGuid(), Name = "Premium Pack Tank" };
        database.Context.Vehicles.AddRange(researchable, premium);

        database.AddVehicleTreeEntry(version, researchable, rank, 5000, treeColumn: 1, treeRow: 2, acquisitionType: VehicleAcquisitionType.Researchable);
        database.AddVehicleTreeEntry(version, premium, rank, 0, treeColumn: 1, treeRow: 1, acquisitionType: VehicleAcquisitionType.Premium);

        // Researchable vehicle requires Premium vehicle as mandatory prerequisite
        database.AddPrerequisite(researchable, premium, version);
        await database.SaveChangesAsync();

        var result = await lifecycleService.ValidateVersionAsync(version.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("cannot have non-researchable vehicle"));
    }

    [Fact]
    public async Task ValidateVersionAsync_EmptyVersion_IsFlaggedAsError()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);
        await database.SaveChangesAsync();

        var result = await lifecycleService.ValidateVersionAsync(version.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("at least one tree rank"));
        Assert.Contains(result.Errors, e => e.Contains("at least one vehicle tree entry"));
    }

    [Fact]
    public async Task PublishVersionAsync_FailsWhenValidationErrorsExist()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);
        var rank = database.AddTreeRank(version, 1);

        var v1 = new Vehicle { Id = Guid.NewGuid(), Name = "Tank A" };
        var v2 = new Vehicle { Id = Guid.NewGuid(), Name = "Tank B" };
        database.Context.Vehicles.AddRange(v1, v2);

        database.AddVehicleTreeEntry(version, v1, rank, 1000, treeColumn: 1, treeRow: 1);
        database.AddVehicleTreeEntry(version, v2, rank, 2000, treeColumn: 2, treeRow: 1);

        // Introduce cycle
        database.AddPrerequisite(v1, v2, version);
        database.AddPrerequisite(v2, v1, version);
        await database.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycleService.PublishVersionAsync(version.Id));

        Assert.Contains("validation failed", exception.Message);
        Assert.Contains("Prerequisite cycle detected", exception.Message);

        // Status must remain Draft
        database.Context.ChangeTracker.Clear();
        var persisted = await database.Context.ResearchTreeVersions.SingleAsync(v => v.Id == version.Id);
        Assert.Equal(ResearchTreeVersionStatus.Draft, persisted.Status);
    }

    [Fact]
    public async Task PublishVersionAsync_SucceedsWhenVersionIsValid()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);
        var rank = database.AddTreeRank(version, 1, 1);

        var v1 = new Vehicle { Id = Guid.NewGuid(), Name = "Valid Tank" };
        database.Context.Vehicles.Add(v1);
        database.AddVehicleTreeEntry(version, v1, rank, 1000, treeColumn: 1, treeRow: 1);
        await database.SaveChangesAsync();

        await lifecycleService.PublishVersionAsync(version.Id);

        database.Context.ChangeTracker.Clear();
        var published = await database.Context.ResearchTreeVersions.SingleAsync(v => v.Id == version.Id);
        Assert.Equal(ResearchTreeVersionStatus.Published, published.Status);
    }

    [Fact]
    public async Task ValidateVersionAsync_FolderCycle_IsFlaggedAsError()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);
        var rank = database.AddTreeRank(version, 1);

        var v1 = new Vehicle { Id = Guid.NewGuid(), Name = "Folder Tank A" };
        var v2 = new Vehicle { Id = Guid.NewGuid(), Name = "Folder Tank B" };
        database.Context.Vehicles.AddRange(v1, v2);

        var e1 = database.AddVehicleTreeEntry(version, v1, rank, 1000, treeColumn: 1, treeRow: 1);
        var e2 = database.AddVehicleTreeEntry(version, v2, rank, 2000, treeColumn: 1, treeRow: 1, folderParent: e1);
        await database.SaveChangesAsync();

        // Introduce folder cycle: e1 parent is e2, e2 parent is e1
        e1.FolderParentEntryId = e2.Id;
        await database.SaveChangesAsync();

        var result = await lifecycleService.ValidateVersionAsync(version.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Folder cycle detected") || e.Contains("cannot itself have a folder parent"));
    }

    [Fact]
    public async Task ValidateVersionAsync_NegativeCostOrCoordinates_IsFlaggedAsError()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);
        var rank = database.AddTreeRank(version, 1);

        var v1 = new Vehicle { Id = Guid.NewGuid(), Name = "Negative Cost Tank" };
        database.Context.Vehicles.Add(v1);

        database.AddVehicleTreeEntry(version, v1, rank, rpCost: -500, slCost: -100, treeColumn: -1, treeRow: -2);
        await database.SaveChangesAsync();

        var result = await lifecycleService.ValidateVersionAsync(version.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("negative RP cost"));
        Assert.Contains(result.Errors, e => e.Contains("negative SL cost"));
        Assert.Contains(result.Errors, e => e.Contains("negative layout coordinates"));
    }
}
