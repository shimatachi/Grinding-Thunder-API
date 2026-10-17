using System.Text;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class TechTreeImportServiceTests
{
    private static async Task<(CalculatorTestDatabase db, ResearchTreeVersion draftVersion)> CreateTestDraftAsync()
    {
        var db = new CalculatorTestDatabase();
        var nation = db.AddNation("Britain");
        var aviation = db.AddVehicleType("Aviation");
        var tree = db.AddResearchTree(nation, aviation);
        var update = db.AddGameUpdate("2.43");
        var draft = db.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);
        await db.SaveChangesAsync();
        return (db, draft);
    }

    [Fact]
    public async Task PreviewAndCommit_ValidCsv_AtomicallyCreatesAllEntities()
    {
        var (db, draft) = await CreateTestDraftAsync();
        using (db)
        {
            var importService = new TechTreeImportService(db.Context);

            var csv = """
                VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow,AcquisitionType,FolderParentVehicleName,PrerequisiteVehicleNames,RequiredVehiclesUnlocked
                Gladiator Mk II,1,4000,1000,1,1,Researchable,,,
                Sea Gladiator Mk II,1,4000,1000,1,2,Researchable,Gladiator Mk II,,
                Hurricane Mk I/L,2,9200,3800,1,3,Researchable,,Gladiator Mk II,1
                """;

            using var previewStream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var preview = await importService.PreviewImportAsync(draft.Id, previewStream, "britain_air.csv");

            Assert.True(preview.IsValid);
            Assert.Empty(preview.Errors);
            Assert.Equal(3, preview.TotalRowsDetected);
            Assert.Equal(3, preview.NewEntriesCount);
            Assert.Equal(0, preview.UpdatedEntriesCount);
            Assert.Equal(3, preview.AutoCreatedVehiclesCount);
            Assert.Equal(2, preview.CreatedRanksCount);

            using var commitStream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var commitResult = await importService.CommitImportAsync(draft.Id, commitStream, "britain_air.csv");

            Assert.True(commitResult.Success);
            Assert.Equal(3, commitResult.ImportedEntriesCount);
            Assert.Equal(3, commitResult.NewEntriesCount);
            Assert.Equal(3, commitResult.AutoCreatedVehiclesCount);
            Assert.Equal(2, commitResult.CreatedRanksCount);

            db.Context.ChangeTracker.Clear();

            // Verify Catalog Vehicles
            var glad = await db.Context.Vehicles.SingleAsync(v => v.Name == "Gladiator Mk II");
            var seaGlad = await db.Context.Vehicles.SingleAsync(v => v.Name == "Sea Gladiator Mk II");
            var hurri = await db.Context.Vehicles.SingleAsync(v => v.Name == "Hurricane Mk I/L");

            // Verify Ranks
            var ranks = await db.Context.TreeRanks
                .Where(r => r.ResearchTreeVersionId == draft.Id)
                .OrderBy(r => r.RankNumber)
                .ToListAsync();
            Assert.Equal(2, ranks.Count);
            Assert.Equal(1, ranks[0].RankNumber);
            Assert.Equal(0, ranks[0].RequiredVehiclesUnlocked);
            Assert.Equal(2, ranks[1].RankNumber);
            Assert.Equal(1, ranks[1].RequiredVehiclesUnlocked);

            // Verify Entries
            var entries = await db.Context.VehicleTreeEntries
                .Include(e => e.Vehicle)
                .Where(e => e.ResearchTreeVersionId == draft.Id)
                .ToListAsync();
            Assert.Equal(3, entries.Count);

            var gladEntry = entries.Single(e => e.VehicleId == glad.Id);
            var seaGladEntry = entries.Single(e => e.VehicleId == seaGlad.Id);
            var hurriEntry = entries.Single(e => e.VehicleId == hurri.Id);

            Assert.Equal(ranks[0].Id, gladEntry.TreeRankId);
            Assert.Equal(4000, gladEntry.RpCost);
            Assert.Equal(1000, gladEntry.SlCost);
            Assert.Null(gladEntry.FolderParentEntryId);

            Assert.Equal(ranks[0].Id, seaGladEntry.TreeRankId);
            Assert.Equal(gladEntry.Id, seaGladEntry.FolderParentEntryId);

            Assert.Equal(ranks[1].Id, hurriEntry.TreeRankId);
            Assert.Equal(9200, hurriEntry.RpCost);

            // Verify Prerequisite edge
            var prereqs = await db.Context.VehiclePrerequisites
                .Where(p => p.ResearchTreeVersionId == draft.Id)
                .ToListAsync();
            Assert.Single(prereqs);
            Assert.Equal(hurriEntry.Id, prereqs[0].VehicleTreeEntryId);
            Assert.Equal(gladEntry.Id, prereqs[0].PrerequisiteVehicleTreeEntryId);
        }
    }

    [Fact]
    public async Task CommitImport_ReimportExistingDraftEntries_UpdatesCostsAndCoordinatesWithoutDuplicates()
    {
        var (db, draft) = await CreateTestDraftAsync();
        using (db)
        {
            var importService = new TechTreeImportService(db.Context);

            var initialCsv = """
                VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow
                Spitfire Mk I,1,10000,50000,1,1
                """;

            using (var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(initialCsv)))
            {
                var res1 = await importService.CommitImportAsync(draft.Id, stream1, "tree.csv");
                Assert.True(res1.Success);
                Assert.Equal(1, res1.NewEntriesCount);
            }

            db.Context.ChangeTracker.Clear();

            // Updated CSV with new RP cost and layout
            var updatedCsv = """
                VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow
                Spitfire Mk I,1,12000,55000,2,2
                """;

            using (var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(updatedCsv)))
            {
                var preview = await importService.PreviewImportAsync(draft.Id, stream2, "tree.csv");
                Assert.True(preview.IsValid);
                Assert.Equal(0, preview.NewEntriesCount);
                Assert.Equal(1, preview.UpdatedEntriesCount);
                Assert.Equal("Update", preview.Entries[0].Action);
            }

            using (var stream3 = new MemoryStream(Encoding.UTF8.GetBytes(updatedCsv)))
            {
                var res2 = await importService.CommitImportAsync(draft.Id, stream3, "tree.csv");
                Assert.True(res2.Success);
                Assert.Equal(0, res2.NewEntriesCount);
                Assert.Equal(1, res2.UpdatedEntriesCount);
            }

            db.Context.ChangeTracker.Clear();
            var entries = await db.Context.VehicleTreeEntries
                .Where(e => e.ResearchTreeVersionId == draft.Id)
                .ToListAsync();

            Assert.Single(entries);
            Assert.Equal(12000, entries[0].RpCost);
            Assert.Equal(55000, entries[0].SlCost);
            Assert.Equal(2, entries[0].TreeColumn);
            Assert.Equal(2, entries[0].TreeRow);
        }
    }

    [Fact]
    public async Task CommitImport_PublishedVersion_RejectsWithConflictException()
    {
        var (db, draft) = await CreateTestDraftAsync();
        using (db)
        {
            var lifecycle = new VersionLifecycleService(db.Context);
            var rank = db.AddTreeRank(draft, 1, 0);
            var v = new Vehicle { Id = Guid.NewGuid(), ResearchTreeId = draft.ResearchTreeId, Name = "Sample" };
            db.Context.Vehicles.Add(v);
            db.AddVehicleTreeEntry(draft, v, rank, 1000, 1000, 1, 1);
            await db.SaveChangesAsync();

            await lifecycle.PublishVersionAsync(draft.Id);

            var importService = new TechTreeImportService(db.Context);
            var csv = """
                VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow
                Sample,1,2000,2000,1,1
                """;

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                importService.CommitImportAsync(draft.Id, stream, "tree.csv"));
        }
    }

    [Fact]
    public async Task PreviewImport_IntraFileRootCoordinateCollision_ReturnsError()
    {
        var (db, draft) = await CreateTestDraftAsync();
        using (db)
        {
            var importService = new TechTreeImportService(db.Context);

            var csv = """
                VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow
                Vehicle A,1,1000,1000,1,1
                Vehicle B,1,1000,1000,1,1
                """;

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var preview = await importService.PreviewImportAsync(draft.Id, stream, "tree.csv");

            Assert.False(preview.IsValid);
            Assert.Contains(preview.Errors, e => e.Contains("Coordinate collision at (1, 1)"));
        }
    }

    [Fact]
    public async Task PreviewImport_CollisionWithExistingDraftRootEntry_ReturnsError()
    {
        var (db, draft) = await CreateTestDraftAsync();
        using (db)
        {
            var rank = db.AddTreeRank(draft, 1, 0);
            var existingVeh = new Vehicle { Id = Guid.NewGuid(), ResearchTreeId = draft.ResearchTreeId, Name = "Existing Vehicle" };
            db.Context.Vehicles.Add(existingVeh);
            db.AddVehicleTreeEntry(draft, existingVeh, rank, 1000, 1000, treeColumn: 2, treeRow: 3);
            await db.SaveChangesAsync();

            var importService = new TechTreeImportService(db.Context);

            var csv = """
                VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow
                New Vehicle,1,2000,2000,2,3
                """;

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var preview = await importService.PreviewImportAsync(draft.Id, stream, "tree.csv");

            Assert.False(preview.IsValid);
            Assert.Contains(preview.Errors, e => e.Contains("Coordinate collision at (2, 3)") && e.Contains("Existing Vehicle"));
        }
    }

    [Fact]
    public async Task PreviewImport_RootEntriesSharingCoordinatesInDifferentRanks_ValidatesCleanly()
    {
        var (db, draft) = await CreateTestDraftAsync();
        using (db)
        {
            var rank1 = db.AddTreeRank(draft, 1, 0);
            var existingVeh = new Vehicle { Id = Guid.NewGuid(), ResearchTreeId = draft.ResearchTreeId, Name = "Existing Rank 1" };
            db.Context.Vehicles.Add(existingVeh);
            db.AddVehicleTreeEntry(draft, existingVeh, rank1, 1000, 1000, treeColumn: 1, treeRow: 1);
            await db.SaveChangesAsync();

            var importService = new TechTreeImportService(db.Context);

            // Import contains:
            // - Rank 2 vehicle at (1, 1): shares coords with existing draft vehicle, but different rank -> allowed.
            // - Rank 3 vehicle at (1, 1): shares coords with imported rank 2 vehicle, but different rank -> allowed.
            var csv = """
                VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow
                New Rank 2,2,2000,2000,1,1
                New Rank 3,3,3000,3000,1,1
                """;

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var preview = await importService.PreviewImportAsync(draft.Id, stream, "tree.csv");

            Assert.True(preview.IsValid);
            Assert.Empty(preview.Errors);
        }
    }

    [Fact]
    public async Task PreviewImport_FolderNestingViolations_ReturnsErrors()
    {
        var (db, draft) = await CreateTestDraftAsync();
        using (db)
        {
            var importService = new TechTreeImportService(db.Context);

            // 1. Self as parent
            // 2. Parent has parent (2-level nesting)
            // 3. Parent and child different ranks
            var csv = """
                VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow,FolderParentVehicleName
                Plane A,1,1000,1000,1,1,Plane A
                Plane Root,1,1000,1000,1,2,
                Plane Child1,1,1000,1000,1,3,Plane Root
                Plane Child2,1,1000,1000,1,4,Plane Child1
                Plane Child3,2,1000,1000,2,1,Plane Root
                """;

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var preview = await importService.PreviewImportAsync(draft.Id, stream, "tree.csv");

            Assert.False(preview.IsValid);
            Assert.Contains(preview.Errors, e => e.Contains("cannot be its own folder parent"));
            Assert.Contains(preview.Errors, e => e.Contains("cannot itself have a folder parent"));
            Assert.Contains(preview.Errors, e => e.Contains("must belong to the same rank"));
        }
    }

    [Fact]
    public async Task PreviewImport_FolderCycle_ReturnsError()
    {
        var (db, draft) = await CreateTestDraftAsync();
        using (db)
        {
            var importService = new TechTreeImportService(db.Context);

            var csv = """
                VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow,FolderParentVehicleName
                Plane A,1,1000,1000,1,1,Plane B
                Plane B,1,1000,1000,1,2,Plane A
                """;

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var preview = await importService.PreviewImportAsync(draft.Id, stream, "tree.csv");

            Assert.False(preview.IsValid);
            Assert.Contains(preview.Errors, e => e.Contains("Folder cycle detected"));
        }
    }

    [Fact]
    public async Task PreviewImport_PrerequisiteIntegrityViolations_ReturnsErrors()
    {
        var (db, draft) = await CreateTestDraftAsync();
        using (db)
        {
            var importService = new TechTreeImportService(db.Context);

            // 1. Self dependency
            // 2. Prerequisite in higher rank (Prereq Rank 2, Entry Rank 1)
            // 3. Researchable depends on Premium
            // 4. Missing prerequisite
            var csv = """
                VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow,AcquisitionType,PrerequisiteVehicleNames
                Plane High,2,5000,5000,2,1,Researchable,
                Plane Prem,1,3000,3000,1,2,Premium,
                Plane A,1,1000,1000,1,1,Researchable,Plane A
                Plane B,1,1000,1000,1,3,Researchable,Plane High
                Plane C,1,1000,1000,1,4,Researchable,Plane Prem
                Plane D,1,1000,1000,1,5,Researchable,Ghost Plane
                """;

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var preview = await importService.PreviewImportAsync(draft.Id, stream, "tree.csv");

            Assert.False(preview.IsValid);
            Assert.Contains(preview.Errors, e => e.Contains("cannot have itself as a prerequisite"));
            Assert.Contains(preview.Errors, e => e.Contains("cannot belong to a higher rank"));
            Assert.Contains(preview.Errors, e => e.Contains("cannot have non-researchable prerequisite"));
            Assert.Contains(preview.Errors, e => e.Contains("does not exist in the import file or draft version"));
        }
    }

    [Fact]
    public async Task PreviewImport_PrerequisiteDagCycle_ReturnsError()
    {
        var (db, draft) = await CreateTestDraftAsync();
        using (db)
        {
            var importService = new TechTreeImportService(db.Context);

            var csv = """
                VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow,PrerequisiteVehicleNames
                Plane A,1,1000,1000,1,1,Plane C
                Plane B,1,1000,1000,1,2,Plane A
                Plane C,1,1000,1000,1,3,Plane B
                """;

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var preview = await importService.PreviewImportAsync(draft.Id, stream, "tree.csv");

            Assert.False(preview.IsValid);
            Assert.Contains(preview.Errors, e => e.Contains("Prerequisite cycle detected"));
        }
    }

    [Fact]
    public async Task PreviewImport_RankFeasibilityViolation_ReturnsError()
    {
        var (db, draft) = await CreateTestDraftAsync();
        using (db)
        {
            var importService = new TechTreeImportService(db.Context);

            // Rank 2 requires 5 unlocked vehicles, but Rank 1 only contains 2 vehicles
            var csv = """
                VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow,RequiredVehiclesUnlocked
                Plane 1,1,1000,1000,1,1,0
                Plane 2,1,1000,1000,1,2,0
                Plane 3,2,5000,5000,2,1,5
                """;

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var preview = await importService.PreviewImportAsync(draft.Id, stream, "tree.csv");

            Assert.False(preview.IsValid);
            Assert.Contains(preview.Errors, e => e.Contains("Rank 2 requires 5 unlocked vehicles, but only 2 eligible vehicles are available"));
        }
    }

    [Fact]
    public async Task PreviewImport_AutoCreateMissingVehiclesToggle_WhenFalse_ReturnsErrors()
    {
        var (db, draft) = await CreateTestDraftAsync();
        using (db)
        {
            var importService = new TechTreeImportService(db.Context);

            var csv = """
                VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow
                Uncataloged Plane,1,1000,1000,1,1
                """;

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var preview = await importService.PreviewImportAsync(draft.Id, stream, "tree.csv", autoCreateMissingVehicles: false);

            Assert.False(preview.IsValid);
            Assert.Contains(preview.Errors, e => e.Contains("does not exist in the vehicle catalog"));
        }
    }

    [Fact]
    public async Task PreviewImport_RankQuotaWithUnspecifiedPrecedingRankQuota_CorrectlyCalculatesPrecedingVehicles()
    {
        var (db, draft) = await CreateTestDraftAsync();
        using (db)
        {
            var importService = new TechTreeImportService(db.Context);

            // Rank 1 vehicles omit RequiredVehiclesUnlocked.
            // Rank 2 has only 1 vehicle, but requires 2 unlocked vehicles (satisfied by the 2 vehicles in Rank 1).
            var csv = """
                VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow,RequiredVehiclesUnlocked
                Plane 1,1,1000,1000,1,1,
                Plane 2,1,1000,1000,1,2,
                Plane 3,2,5000,5000,2,1,2
                """;

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var preview = await importService.PreviewImportAsync(draft.Id, stream, "tree.csv");

            Assert.True(preview.IsValid);
            Assert.Empty(preview.Errors);
        }
    }
}
