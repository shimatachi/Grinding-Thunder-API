using System.Text;
using GrindingThunder.Api.Application.Exceptions;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using GrindingThunder.Api.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class VehicleOwnershipTests
{
    [Fact]
    public async Task Vehicle_RequiresAnExistingResearchTree()
    {
        using var db = new CalculatorTestDatabase();
        db.Context.Vehicles.Add(new Vehicle { Id = Guid.NewGuid(), Name = "Unowned" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Context.ChangeTracker.Clear();
        db.Context.Vehicles.Add(new Vehicle { Id = Guid.NewGuid(), ResearchTreeId = Guid.NewGuid(), Name = "Unknown tree" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameName_InDifferentNationsOrCategories_IsAllowed(bool differentCategory)
    {
        using var db = new CalculatorTestDatabase();
        var nation = db.AddNation("USA");
        var ground = db.AddVehicleType("Ground");
        var first = db.AddResearchTree(nation, ground);
        var second = differentCategory
            ? db.AddResearchTree(nation, db.AddVehicleType("Aviation"))
            : db.AddResearchTree(db.AddNation("Germany"), ground);
        db.Context.Vehicles.AddRange(
            new Vehicle { Id = Guid.NewGuid(), ResearchTreeId = first.Id, Name = "Shared" },
            new Vehicle { Id = Guid.NewGuid(), ResearchTreeId = second.Id, Name = "SHARED" });
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        var vehicles = await db.Context.Vehicles.Include(v => v.ResearchTree).ToListAsync();
        Assert.Equal(2, vehicles.Count);
        Assert.Equal(2, vehicles.Select(v => v.ResearchTreeId).Distinct().Count());
        Assert.All(vehicles, v => Assert.Equal(v.ResearchTreeId, v.ResearchTree.Id));
    }

    [Theory]
    [InlineData("Tank")]
    [InlineData("tAnK")]
    [InlineData(" TANK ")]
    public async Task SameTree_NormalizedDuplicate_IsRejectedByDatabase(string duplicateName)
    {
        using var db = new CalculatorTestDatabase();
        var tree = db.AddResearchTree(db.AddNation(), db.AddVehicleType("Ground"));
        db.Context.Vehicles.Add(new Vehicle { Id = Guid.NewGuid(), ResearchTreeId = tree.Id, Name = "Tank" });
        await db.SaveChangesAsync();
        db.Context.Vehicles.Add(new Vehicle { Id = Guid.NewGuid(), ResearchTreeId = tree.Id, Name = duplicateName });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CrossTreeEntry_BothCompositeForeignKeysRejectDirectSql(bool carryVehicleTree)
    {
        using var db = new CalculatorTestDatabase();
        var rank = db.AddTreeRank(db.AddNation("USA"), 1);
        var otherRank = db.AddTreeRank(db.AddNation("Germany"), 1);
        var vehicle = db.AddVehicle(otherRank, "Foreign vehicle", 100);
        await db.SaveChangesAsync();
        var treeId = carryVehicleTree ? vehicle.ResearchTreeId : rank.ResearchTreeVersion.ResearchTreeId;
        // Bypass navigation fixup and all controller/service validation.
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            db.Context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "VehicleTreeEntries"
                    ("Id", "ResearchTreeId", "ResearchTreeVersionId", "VehicleId", "TreeRankId", "RpCost", "SlCost", "TreeColumn", "TreeRow")
                VALUES ({Guid.NewGuid()}, {treeId}, {rank.ResearchTreeVersionId}, {vehicle.Id}, {rank.Id}, 1, 1, 1, 1)
                """));
    }

    [Fact]
    public async Task TreeDeletion_IsRestrictedEvenForEntrylessVehicle()
    {
        using var db = new CalculatorTestDatabase();
        var tree = db.AddResearchTree(db.AddNation(), db.AddVehicleType("Ground"));
        db.Context.Vehicles.Add(new Vehicle { Id = Guid.NewGuid(), ResearchTreeId = tree.Id, Name = "Entryless" });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            db.Context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"ResearchTrees\" WHERE \"Id\" = {tree.Id}"));
        Assert.Equal(1, await db.Context.Vehicles.CountAsync());
    }

    [Fact]
    public async Task Import_MatchesOnlyInsideSelectedTree_WhenNamesExistInBothTrees()
    {
        using var db = new CalculatorTestDatabase();
        var rank = db.AddTreeRank(db.AddNation("USA"), 1);
        var otherRank = db.AddTreeRank(db.AddNation("Germany"), 1);
        var local = db.AddVehicle(rank, "Shared", 100);
        var foreign = db.AddVehicle(otherRank, "Shared", 900);
        await db.SaveChangesAsync();
        var service = new TechTreeImportService(db.Context);
        using var previewFile = Csv("Shared");
        var preview = await service.PreviewImportAsync(rank.ResearchTreeVersionId, previewFile, "tree.csv", false);
        Assert.True(preview.IsValid);
        Assert.Equal(1, preview.UpdatedEntriesCount);
        Assert.Equal(0, preview.AutoCreatedVehiclesCount);
        using var commitFile = Csv("Shared");
        Assert.True((await service.CommitImportAsync(rank.ResearchTreeVersionId, commitFile, "tree.csv", false)).Success);
        db.Context.ChangeTracker.Clear();
        Assert.Equal(200, (await db.Context.VehicleTreeEntries.SingleAsync(e => e.VehicleId == local.Id)).RpCost);
        Assert.Equal(900, (await db.Context.VehicleTreeEntries.SingleAsync(e => e.VehicleId == foreign.Id)).RpCost);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Import_ForeignName_IsMissing_AndAutoCreationUsesSelectedTree(bool autoCreate)
    {
        using var db = new CalculatorTestDatabase();
        var rank = db.AddTreeRank(db.AddNation("USA"), 1);
        var otherRank = db.AddTreeRank(db.AddNation("Germany"), 1);
        var foreign = db.AddVehicle(otherRank, "Shared", 900);
        await db.SaveChangesAsync();
        var service = new TechTreeImportService(db.Context);
        using var previewFile = Csv("Shared");
        var preview = await service.PreviewImportAsync(rank.ResearchTreeVersionId, previewFile, "tree.csv", autoCreate);
        Assert.Equal(autoCreate, preview.IsValid);
        Assert.Equal(autoCreate ? 1 : 0, preview.AutoCreatedVehiclesCount);
        Assert.Equal(1, await db.Context.Vehicles.CountAsync());
        using var commitFile = Csv("Shared");
        var result = await service.CommitImportAsync(rank.ResearchTreeVersionId, commitFile, "tree.csv", autoCreate);
        Assert.Equal(autoCreate, result.Success);
        db.Context.ChangeTracker.Clear();
        if (autoCreate)
        {
            var entry = await db.Context.VehicleTreeEntries.Include(e => e.Vehicle)
                .SingleAsync(e => e.ResearchTreeVersionId == rank.ResearchTreeVersionId);
            Assert.Equal(rank.ResearchTreeVersion.ResearchTreeId, entry.Vehicle.ResearchTreeId);
            Assert.Equal(entry.ResearchTreeId, entry.Vehicle.ResearchTreeId);
            Assert.NotEqual(foreign.Id, entry.VehicleId);
        }
        else
        {
            Assert.Equal(1, await db.Context.Vehicles.CountAsync());
            Assert.False(await db.Context.VehicleTreeEntries.AnyAsync(e => e.ResearchTreeVersionId == rank.ResearchTreeVersionId));
        }
    }

    [Fact]
    public async Task Import_EntryFailure_RollsBackAutoCreatedIdentityAndRanks()
    {
        using var db = new CalculatorTestDatabase();
        var tree = db.AddResearchTree(db.AddNation(), db.AddVehicleType("Ground"));
        var version = db.AddResearchTreeVersion(tree, db.AddGameUpdate("test"));
        await db.SaveChangesAsync();
        await db.Context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_entry BEFORE INSERT ON "VehicleTreeEntries"
            BEGIN SELECT RAISE(ABORT, 'Simulated entry failure'); END;
            """);
        using var file = Csv("New");
        await Assert.ThrowsAsync<DbUpdateException>(() => new TechTreeImportService(db.Context)
            .CommitImportAsync(version.Id, file, "tree.csv", true));
        db.Context.ChangeTracker.Clear();
        Assert.Empty(await db.Context.Vehicles.ToListAsync());
        Assert.Empty(await db.Context.TreeRanks.ToListAsync());
        Assert.Empty(await db.Context.VehicleTreeEntries.ToListAsync());
    }

    [Fact]
    public async Task PlayerStableIds_SurviveClonePublish_AndIntersectWithExactVersion()
    {
        using var db = new CalculatorTestDatabase();
        var rank = db.AddTreeRank(db.AddNation("USA"), 1);
        var otherRank = db.AddTreeRank(db.AddNation("Germany"), 1);
        var owned = db.AddVehicle(rank, "Owned", 50);
        var target = db.AddVehicle(rank, "Target", 100);
        var foreignOwned = db.AddVehicle(otherRank, "Owned", 900);
        target.TreeEntries.Single().TreeColumn = 1;
        var player = new PlayerAccount
        {
            Id = Guid.NewGuid(), Username = "player", NormalizedUsername = "PLAYER",
            Email = "player@example.com", NormalizedEmail = "PLAYER@EXAMPLE.COM", PasswordHash = "unused"
        };
        db.Context.PlayerAccounts.Add(player);
        var nextUpdate = db.AddGameUpdate("next");
        await db.SaveChangesAsync();
        var progress = new PlayerProgressService(db.Context);
        Assert.NotNull((await progress.UpdateAsync(player.Id, new([owned.Id, foreignOwned.Id], new()))).Progress);
        var lifecycle = new VersionLifecycleService(db.Context);
        await lifecycle.PublishVersionAsync(rank.ResearchTreeVersionId);
        var clone = await lifecycle.CloneVersionAsync(rank.ResearchTreeVersionId, nextUpdate.Id);
        (await db.Context.VehicleTreeEntries.SingleAsync(e => e.ResearchTreeVersionId == clone.Id && e.VehicleId == target.Id)).RpCost = 400;
        await db.SaveChangesAsync();
        await lifecycle.PublishVersionAsync(clone.Id);
        db.Context.ChangeTracker.Clear();
        var savedIds = (await progress.GetAsync(player.Id))!.UnlockedVehicleIds;
        Assert.Equal(new[] { owned.Id, foreignOwned.Id }.Order(), savedIds);
        foreach (var (versionId, expectedRp) in new[] { (rank.ResearchTreeVersionId, 100L), (clone.Id, 400L) })
        {
            var intersection = await db.Context.VehicleTreeEntries
                .Where(e => e.ResearchTreeVersionId == versionId && savedIds.Contains(e.VehicleId))
                .Select(e => e.VehicleId).ToListAsync();
            Assert.Equal(owned.Id, Assert.Single(intersection));
            var calculation = await db.Calculator.CalculateResearchAsync(new(versionId, target.Id, intersection, [], 100, 100));
            Assert.Equal(expectedRp, calculation.TotalRpRequired);
        }
        await Assert.ThrowsAsync<ResearchCalculationInputException>(() => db.Calculator.CalculateResearchAsync(
            new(clone.Id, target.Id, savedIds, [], 100, 100)));
        var diff = await lifecycle.CompareVersionsAsync(rank.ResearchTreeVersionId, clone.Id);
        Assert.Equal(target.Id, Assert.Single(diff.ModifiedVehicles).VehicleId);
    }

    [Fact]
    public void Migration_GuardsUnsafeOwnershipAndDuplicatesBeforeRequiredConstraints()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused").Options);
        var migrator = db.GetService<IMigrator>();
        var script = migrator.GenerateScript("20261013145650_AddPlayerAccounts", "20261015142100_ScopeVehiclesToResearchTree");
        Assert.Contains("count(DISTINCT version.\"ResearchTreeId\") <> 1", script);
        Assert.Contains("unowned or multi-tree vehicle(s)", script);
        Assert.Contains("duplicate normalized name group(s)", script);
        Assert.Contains("LIMIT 20", script);
        Assert.True(script.IndexOf("unowned or multi-tree", StringComparison.Ordinal) < script.IndexOf("UPDATE \"Vehicles\"", StringComparison.Ordinal));
        Assert.True(script.IndexOf("UPDATE \"Vehicles\"", StringComparison.Ordinal) < script.IndexOf("SET NOT NULL", StringComparison.Ordinal));
        Assert.Contains("FOREIGN KEY (\"ResearchTreeId\", \"VehicleId\")", script);
        Assert.Contains("FOREIGN KEY (\"ResearchTreeId\", \"ResearchTreeVersionId\")", script);
        Assert.DoesNotContain("DELETE FROM \"PlayerUnlockedVehicles\"", script);
        Assert.DoesNotContain("DELETE FROM \"Vehicles\"", script);
        var down = migrator.GenerateScript("20261015142100_ScopeVehiclesToResearchTree", "20261013145650_AddPlayerAccounts");
        Assert.Contains("entryless vehicles would lose their only ownership mapping", down);
    }

    private static MemoryStream Csv(string name) => new(Encoding.UTF8.GetBytes(
        $"VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow\n{name},1,200,300,1,1"));
}
