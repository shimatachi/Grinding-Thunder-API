using GrindingThunder.Api.Application.Exceptions;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using GrindingThunder.Api.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class VersionLifecycleServiceTests
{
    [Fact]
    public async Task CloneVersionAsync_ProducesCompleteIsolatedDeepCopy_UnderDraftStatus()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var nation = database.AddNation("USA");
        var ground = database.AddVehicleType("Ground");
        var tree = database.AddResearchTree(nation, ground);
        var update1 = database.AddGameUpdate("2.43");
        var update2 = database.AddGameUpdate("2.45");

        var sourceVersion = database.AddResearchTreeVersion(tree, update1, ResearchTreeVersionStatus.Draft);
        var rank1 = database.AddTreeRank(sourceVersion, 1, 2);
        var rank2 = database.AddTreeRank(sourceVersion, 2, 2);

        var veh1 = new Vehicle { Id = Guid.NewGuid(), Name = "M2 Light" };
        var veh2 = new Vehicle { Id = Guid.NewGuid(), Name = "M3 Stuart" };
        var veh3 = new Vehicle { Id = Guid.NewGuid(), Name = "M3A1 Stuart" };
        database.Context.Vehicles.AddRange(veh1, veh2, veh3);

        var entry1 = database.AddVehicleTreeEntry(sourceVersion, veh1, rank1, 2900, 700, treeColumn: 1, treeRow: 1, acquisitionType: VehicleAcquisitionType.Researchable);
        var entry2 = database.AddVehicleTreeEntry(sourceVersion, veh2, rank1, 4000, 1400, treeColumn: 1, treeRow: 2, folderParent: entry1, acquisitionType: VehicleAcquisitionType.Researchable);
        var entry3 = database.AddVehicleTreeEntry(sourceVersion, veh3, rank2, 9200, 3800, treeColumn: 1, treeRow: 3, acquisitionType: VehicleAcquisitionType.Premium);

        database.AddPrerequisite(veh2, veh1, sourceVersion);
        database.AddPrerequisite(veh3, veh2, sourceVersion);

        await database.SaveChangesAsync();
        await lifecycleService.PublishVersionAsync(sourceVersion.Id);

        database.Context.ChangeTracker.Clear();

        // Act: Clone published source version into update2
        var cloned = await lifecycleService.CloneVersionAsync(sourceVersion.Id, update2.Id);

        database.Context.ChangeTracker.Clear();

        // Assert: New version exists in Draft status
        var persistedClone = await database.Context.ResearchTreeVersions
            .AsNoTracking()
            .Include(v => v.TreeRanks)
            .Include(v => v.Entries)
            .SingleAsync(v => v.Id == cloned.Id);

        Assert.NotEqual(sourceVersion.Id, persistedClone.Id);
        Assert.Equal(tree.Id, persistedClone.ResearchTreeId);
        Assert.Equal(update2.Id, persistedClone.GameUpdateId);
        Assert.Equal(ResearchTreeVersionStatus.Draft, persistedClone.Status);

        // Assert: Deep-cloned ranks
        Assert.Equal(2, persistedClone.TreeRanks.Count);
        Assert.All(persistedClone.TreeRanks, r => Assert.Equal(cloned.Id, r.ResearchTreeVersionId));
        Assert.DoesNotContain(persistedClone.TreeRanks, r => r.Id == rank1.Id || r.Id == rank2.Id);

        var clonedRank1 = persistedClone.TreeRanks.Single(r => r.RankNumber == 1);
        var clonedRank2 = persistedClone.TreeRanks.Single(r => r.RankNumber == 2);
        Assert.Equal(2, clonedRank1.RequiredVehiclesUnlocked);
        Assert.Equal(2, clonedRank2.RequiredVehiclesUnlocked);

        // Assert: Deep-cloned entries with layout and acquisition types
        Assert.Equal(3, persistedClone.Entries.Count);
        Assert.All(persistedClone.Entries, e => Assert.Equal(cloned.Id, e.ResearchTreeVersionId));
        Assert.DoesNotContain(persistedClone.Entries, e => e.Id == entry1.Id || e.Id == entry2.Id || e.Id == entry3.Id);

        var clonedEntry1 = persistedClone.Entries.Single(e => e.VehicleId == veh1.Id);
        var clonedEntry2 = persistedClone.Entries.Single(e => e.VehicleId == veh2.Id);
        var clonedEntry3 = persistedClone.Entries.Single(e => e.VehicleId == veh3.Id);

        Assert.Equal(clonedRank1.Id, clonedEntry1.TreeRankId);
        Assert.Equal(clonedRank1.Id, clonedEntry2.TreeRankId);
        Assert.Equal(clonedRank2.Id, clonedEntry3.TreeRankId);

        Assert.Equal(2900, clonedEntry1.RpCost);
        Assert.Equal(700, clonedEntry1.SlCost);
        Assert.Equal(1, clonedEntry1.TreeColumn);
        Assert.Equal(1, clonedEntry1.TreeRow);
        Assert.Equal(VehicleAcquisitionType.Researchable, clonedEntry1.AcquisitionType);
        Assert.Null(clonedEntry1.FolderParentEntryId);

        Assert.Equal(clonedEntry1.Id, clonedEntry2.FolderParentEntryId);
        Assert.Equal(1, clonedEntry2.TreeColumn);
        Assert.Equal(2, clonedEntry2.TreeRow);

        Assert.Equal(VehicleAcquisitionType.Premium, clonedEntry3.AcquisitionType);

        // Assert: Deep-cloned prerequisites
        var clonedPrereqs = await database.Context.VehiclePrerequisites
            .AsNoTracking()
            .Where(p => p.ResearchTreeVersionId == cloned.Id)
            .ToListAsync();

        Assert.Equal(2, clonedPrereqs.Count);
        Assert.Contains(clonedPrereqs, p => p.VehicleTreeEntryId == clonedEntry2.Id && p.PrerequisiteVehicleTreeEntryId == clonedEntry1.Id);
        Assert.Contains(clonedPrereqs, p => p.VehicleTreeEntryId == clonedEntry3.Id && p.PrerequisiteVehicleTreeEntryId == clonedEntry2.Id);
    }

    [Fact]
    public async Task CloneVersionAsync_CompositeForeignKeys_PreventCrossVersionLeakage()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update1 = database.AddGameUpdate("2.43");
        var update2 = database.AddGameUpdate("2.45");

        var sourceVersion = database.AddResearchTreeVersion(tree, update1);
        var rank = database.AddTreeRank(sourceVersion, 1);
        var veh1 = new Vehicle { Id = Guid.NewGuid(), Name = "Tank 1" };
        var veh2 = new Vehicle { Id = Guid.NewGuid(), Name = "Tank 2" };
        database.Context.Vehicles.AddRange(veh1, veh2);
        var entry1 = database.AddVehicleTreeEntry(sourceVersion, veh1, rank, 100);
        var entry2 = database.AddVehicleTreeEntry(sourceVersion, veh2, rank, 200);
        database.AddPrerequisite(veh2, veh1, sourceVersion);
        await database.SaveChangesAsync();

        var cloned = await lifecycleService.CloneVersionAsync(sourceVersion.Id, update2.Id);

        // Attempting to create a cross-version prerequisite (cloned entry -> source entry) fails composite FK
        var clonedEntry2 = await database.Context.VehicleTreeEntries
            .SingleAsync(e => e.ResearchTreeVersionId == cloned.Id && e.VehicleId == veh2.Id);

        database.Context.VehiclePrerequisites.Add(new VehiclePrerequisite
        {
            ResearchTreeVersionId = cloned.Id,
            VehicleTreeEntryId = clonedEntry2.Id,
            PrerequisiteVehicleTreeEntryId = entry1.Id // belongs to sourceVersion, not cloned!
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
    }

    [Fact]
    public async Task CloneVersionAsync_ExistingTreeAndGameUpdatePair_Fails()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update1 = database.AddGameUpdate("2.43");
        var update2 = database.AddGameUpdate("2.45");

        var version1 = database.AddResearchTreeVersion(tree, update1);
        var version2 = database.AddResearchTreeVersion(tree, update2);
        await database.SaveChangesAsync();

        // Attempt to clone version1 into update2 (which already has version2 for this tree)
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycleService.CloneVersionAsync(version1.Id, update2.Id));

        Assert.Contains("already exists", exception.Message);
    }

    [Fact]
    public async Task CloneVersionAsync_NonExistentSourceVersion_ThrowsNotFound()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);
        var update = database.AddGameUpdate("2.43");
        await database.SaveChangesAsync();

        var nonExistentId = Guid.NewGuid();
        await Assert.ThrowsAsync<ResearchTreeVersionNotFoundException>(
            () => lifecycleService.CloneVersionAsync(nonExistentId, update.Id));
    }

    [Fact]
    public async Task CloneVersionAsync_NonExistentTargetGameUpdate_ThrowsInvalidOperationException()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);
        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree, update);
        await database.SaveChangesAsync();

        var nonExistentUpdateId = Guid.NewGuid();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycleService.CloneVersionAsync(version.Id, nonExistentUpdateId));

        Assert.Contains("was not found", exception.Message);
    }

    [Fact]
    public async Task PublishVersionAsync_TransitionsDraftToPublished()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var draft = database.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);
        var rank = database.AddTreeRank(draft, 1, 1);
        var veh = new Vehicle { Id = Guid.NewGuid(), Name = "Tank" };
        database.Context.Vehicles.Add(veh);
        database.AddVehicleTreeEntry(draft, veh, rank, 1000);
        await database.SaveChangesAsync();

        await lifecycleService.PublishVersionAsync(draft.Id);

        database.Context.ChangeTracker.Clear();
        var published = await database.Context.ResearchTreeVersions.SingleAsync(v => v.Id == draft.Id);
        Assert.Equal(ResearchTreeVersionStatus.Published, published.Status);
    }

    [Fact]
    public async Task PublishVersionAsync_AlreadyPublishedVersion_ThrowsInvalidOperationException()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);
        var rank = database.AddTreeRank(version, 1, 1);
        var veh = new Vehicle { Id = Guid.NewGuid(), Name = "Tank" };
        database.Context.Vehicles.Add(veh);
        database.AddVehicleTreeEntry(version, veh, rank, 1000);
        await database.SaveChangesAsync();

        await lifecycleService.PublishVersionAsync(version.Id);

        // Attempting to publish again fails
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycleService.PublishVersionAsync(version.Id));

        Assert.Contains("already published", exception.Message);
    }

    [Fact]
    public async Task PublishVersionAsync_NonExistentVersion_ThrowsNotFound()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var nonExistentId = Guid.NewGuid();
        await Assert.ThrowsAsync<ResearchTreeVersionNotFoundException>(
            () => lifecycleService.PublishVersionAsync(nonExistentId));
    }

    [Fact]
    public async Task DeleteDraftVersionAsync_DeletesDraftVersionAndCascadesChildren()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var draft = database.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);
        var rank = database.AddTreeRank(draft, 1);
        var veh = new Vehicle { Id = Guid.NewGuid(), Name = "Tank" };
        database.Context.Vehicles.Add(veh);
        database.AddVehicleTreeEntry(draft, veh, rank, 100);
        await database.SaveChangesAsync();

        database.Context.ChangeTracker.Clear();

        await lifecycleService.DeleteDraftVersionAsync(draft.Id);

        database.Context.ChangeTracker.Clear();
        Assert.Null(await database.Context.ResearchTreeVersions.FindAsync(draft.Id));
        Assert.Empty(await database.Context.TreeRanks.Where(r => r.ResearchTreeVersionId == draft.Id).ToListAsync());
        Assert.Empty(await database.Context.VehicleTreeEntries.Where(e => e.ResearchTreeVersionId == draft.Id).ToListAsync());
    }

    [Fact]
    public async Task DeleteDraftVersionAsync_OnPublishedVersion_IsBlocked()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);
        var rank = database.AddTreeRank(version, 1, 1);
        var veh = new Vehicle { Id = Guid.NewGuid(), Name = "Tank" };
        database.Context.Vehicles.Add(veh);
        database.AddVehicleTreeEntry(version, veh, rank, 1000);
        await database.SaveChangesAsync();

        await lifecycleService.PublishVersionAsync(version.Id);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycleService.DeleteDraftVersionAsync(version.Id));

        Assert.Contains("published", exception.Message);
    }

    [Fact]
    public async Task MutatingPublishedVersion_ThrowsImmutabilityException()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);
        var rank = database.AddTreeRank(version, 1, 1);
        var veh = new Vehicle { Id = Guid.NewGuid(), Name = "Tank" };
        database.Context.Vehicles.Add(veh);
        var entry = database.AddVehicleTreeEntry(version, veh, rank, 1000);
        await database.SaveChangesAsync();

        await lifecycleService.PublishVersionAsync(version.Id);
        database.Context.ChangeTracker.Clear();

        // 1. Modifying entry RP cost on published version throws
        var trackedEntry = await database.Context.VehicleTreeEntries.SingleAsync(e => e.Id == entry.Id);
        trackedEntry.RpCost = 2000;
        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        Assert.Contains("published and immutable", ex1.Message);

        database.Context.ChangeTracker.Clear();

        // 2. Modifying TreeRank required vehicles on published version throws
        var trackedRank = await database.Context.TreeRanks.SingleAsync(r => r.Id == rank.Id);
        trackedRank.RequiredVehiclesUnlocked = 5;
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        Assert.Contains("published and immutable", ex2.Message);

        database.Context.ChangeTracker.Clear();

        // 3. Adding a new entry to published version throws
        var newVeh = new Vehicle { Id = Guid.NewGuid(), Name = "New Tank" };
        database.Context.Vehicles.Add(newVeh);
        database.Context.VehicleTreeEntries.Add(new VehicleTreeEntry
        {
            Id = Guid.NewGuid(),
            ResearchTreeVersionId = version.Id,
            VehicleId = newVeh.Id,
            TreeRankId = rank.Id,
            RpCost = 500
        });
        var ex3 = await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        Assert.Contains("published and immutable", ex3.Message);

        database.Context.ChangeTracker.Clear();

        // 4. Deleting an entry on published version throws
        var entryToDelete = await database.Context.VehicleTreeEntries.SingleAsync(e => e.Id == entry.Id);
        database.Context.VehicleTreeEntries.Remove(entryToDelete);
        var ex4 = await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        Assert.Contains("published and immutable", ex4.Message);

        database.Context.ChangeTracker.Clear();

        // 5. Deleting published version itself directly throws
        var versionToDelete = await database.Context.ResearchTreeVersions.SingleAsync(v => v.Id == version.Id);
        database.Context.ResearchTreeVersions.Remove(versionToDelete);
        var ex5 = await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        Assert.Contains("immutable and cannot be deleted", ex5.Message);

        database.Context.ChangeTracker.Clear();

        // 6. Adding a prerequisite to published version throws
        var veh2 = new Vehicle { Id = Guid.NewGuid(), Name = "Tank 2" };
        database.Context.Vehicles.Add(veh2);
        // Note: entry2 can't be added directly to published version, so prerequisite cannot be added to published version
        database.Context.VehiclePrerequisites.Add(new VehiclePrerequisite
        {
            ResearchTreeVersionId = version.Id,
            VehicleTreeEntryId = entry.Id,
            PrerequisiteVehicleTreeEntryId = entry.Id // even dummy edge throws immutability first
        });
        var ex6 = await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        Assert.Contains("published and immutable", ex6.Message);
    }

    [Fact]
    public async Task PublishingVersion_WithNonStatusPropertyModifications_ThrowsInvalidOperationException()
    {
        using var database = new CalculatorTestDatabase();
        var tree1 = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var tree2 = database.AddResearchTree(database.AddNation("Germany"), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree1, update, ResearchTreeVersionStatus.Draft);
        await database.SaveChangesAsync();

        // Attempting to modify ResearchTreeId while transitioning to Published
        version.Status = ResearchTreeVersionStatus.Published;
        version.ResearchTreeId = tree2.Id;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        Assert.Contains("Cannot modify properties other than Status", exception.Message);
    }
}
