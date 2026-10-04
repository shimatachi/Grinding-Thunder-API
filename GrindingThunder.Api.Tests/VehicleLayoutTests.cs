using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class VehicleLayoutTests
{
    [Fact]
    public void LayoutAndFolderState_BelongsToVehicleTreeEntry_NotVehicle()
    {
        var vehicleProperties = typeof(Vehicle).GetProperties().Select(p => p.Name).ToHashSet();
        var entryProperties = typeof(VehicleTreeEntry).GetProperties().Select(p => p.Name).ToHashSet();

        Assert.DoesNotContain(nameof(VehicleTreeEntry.TreeRow), vehicleProperties);
        Assert.DoesNotContain(nameof(VehicleTreeEntry.TreeColumn), vehicleProperties);
        Assert.DoesNotContain(nameof(VehicleTreeEntry.FolderParentEntryId), vehicleProperties);
        Assert.DoesNotContain("IsFolderParent", vehicleProperties);

        Assert.Contains(nameof(VehicleTreeEntry.TreeRow), entryProperties);
        Assert.Contains(nameof(VehicleTreeEntry.TreeColumn), entryProperties);
        Assert.Contains(nameof(VehicleTreeEntry.FolderParentEntryId), entryProperties);
    }

    [Fact]
    public async Task SameVehicle_CanHaveDifferentCoordinatesInDifferentVersions()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var tree = database.AddResearchTree(nation, database.AddVehicleType("Ground"));
        var versionA = database.AddResearchTreeVersion(tree, database.AddGameUpdate("2.43"));
        var versionB = database.AddResearchTreeVersion(tree, database.AddGameUpdate("2.45"));
        var rankA = database.AddTreeRank(versionA, 1);
        var rankB = database.AddTreeRank(versionB, 1);
        var vehicle = new Vehicle { Id = Guid.NewGuid(), Name = "Stable vehicle" };
        database.Context.Vehicles.Add(vehicle);

        database.AddVehicleTreeEntry(versionA, vehicle, rankA, 100, treeColumn: 1, treeRow: 2);
        database.AddVehicleTreeEntry(versionB, vehicle, rankB, 100, treeColumn: 4, treeRow: 7);
        await database.SaveChangesAsync();

        var layouts = await database.Context.VehicleTreeEntries
            .AsNoTracking()
            .Where(e => e.VehicleId == vehicle.Id)
            .OrderBy(e => e.TreeColumn)
            .Select(e => new { e.TreeColumn, e.TreeRow })
            .ToListAsync();

        Assert.Equal(2, layouts.Count);
        Assert.Equal((1, 2), (layouts[0].TreeColumn, layouts[0].TreeRow));
        Assert.Equal((4, 7), (layouts[1].TreeColumn, layouts[1].TreeRow));
    }

    [Fact]
    public async Task FolderMembership_CanDifferBetweenVersions()
    {
        using var database = new CalculatorTestDatabase();
        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var versionA = database.AddResearchTreeVersion(tree, database.AddGameUpdate("2.43"));
        var versionB = database.AddResearchTreeVersion(tree, database.AddGameUpdate("2.45"));
        var rankA = database.AddTreeRank(versionA, 1);
        var rankB = database.AddTreeRank(versionB, 1);
        var parent = new Vehicle { Id = Guid.NewGuid(), Name = "Folder parent" };
        var child = new Vehicle { Id = Guid.NewGuid(), Name = "Folder child" };
        database.Context.Vehicles.AddRange(parent, child);

        var parentA = database.AddVehicleTreeEntry(versionA, parent, rankA, 100);
        database.AddVehicleTreeEntry(versionA, child, rankA, 100, folderParent: parentA);
        database.AddVehicleTreeEntry(versionB, parent, rankB, 100);
        database.AddVehicleTreeEntry(versionB, child, rankB, 100);
        await database.SaveChangesAsync();

        var childEntries = await database.Context.VehicleTreeEntries
            .AsNoTracking()
            .Where(e => e.VehicleId == child.Id)
            .ToDictionaryAsync(e => e.ResearchTreeVersionId);

        Assert.Equal(parentA.Id, childEntries[versionA.Id].FolderParentEntryId);
        Assert.Null(childEntries[versionB.Id].FolderParentEntryId);
    }

    [Fact]
    public async Task CrossVersionFolderParent_IsRejectedByDatabase()
    {
        using var database = new CalculatorTestDatabase();
        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var versionA = database.AddResearchTreeVersion(tree, database.AddGameUpdate("2.43"));
        var versionB = database.AddResearchTreeVersion(tree, database.AddGameUpdate("2.45"));
        var rankA = database.AddTreeRank(versionA, 1);
        var rankB = database.AddTreeRank(versionB, 1);
        var parent = new Vehicle { Id = Guid.NewGuid(), Name = "Folder parent" };
        var child = new Vehicle { Id = Guid.NewGuid(), Name = "Folder child" };
        database.Context.Vehicles.AddRange(parent, child);
        var parentB = database.AddVehicleTreeEntry(versionB, parent, rankB, 100);
        var childA = database.AddVehicleTreeEntry(versionA, child, rankA, 100);
        await database.SaveChangesAsync();

        childA.FolderParentEntryId = parentB.Id;

        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
    }

    [Fact]
    public async Task FolderPlacement_DoesNotCreateOrImplyPrerequisiteEdge()
    {
        using var database = new CalculatorTestDatabase();
        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var version = database.AddResearchTreeVersion(tree, database.AddGameUpdate("2.43"));
        var rank = database.AddTreeRank(version, 1);
        var parent = new Vehicle { Id = Guid.NewGuid(), Name = "Folder parent" };
        var child = new Vehicle { Id = Guid.NewGuid(), Name = "Folder child" };
        database.Context.Vehicles.AddRange(parent, child);
        var parentEntry = database.AddVehicleTreeEntry(version, parent, rank, 10, treeColumn: 1, treeRow: 1);
        database.AddVehicleTreeEntry(version, child, rank, 20, treeColumn: 1, treeRow: 2, folderParent: parentEntry);
        await database.SaveChangesAsync();

        Assert.Empty(await database.Context.VehiclePrerequisites.AsNoTracking().ToListAsync());

        var result = await database.Calculator.CalculateResearchAsync(
            new ResearchCalculationRequest(version.Id, child.Id, [], [], 100));

        var required = Assert.Single(result.RequiredVehicles);
        Assert.Equal(child.Id, required.VehicleId);
        Assert.Equal(20, result.TotalRpRequired);
    }

    [Fact]
    public async Task VehicleApi_ProjectsLegacyLayoutShapeFromSingleVersionEntry()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var tree = database.AddResearchTree(nation, database.AddVehicleType("Ground"));
        var version = database.AddResearchTreeVersion(tree, database.AddGameUpdate("2.43"));
        var rank = database.AddTreeRank(version, 1);
        var parent = new Vehicle { Id = Guid.NewGuid(), Name = "Folder parent" };
        var child = new Vehicle { Id = Guid.NewGuid(), Name = "Folder child" };
        database.Context.Vehicles.AddRange(parent, child);
        var parentEntry = database.AddVehicleTreeEntry(version, parent, rank, 10, treeColumn: 3, treeRow: 4);
        database.AddVehicleTreeEntry(version, child, rank, 20, treeColumn: 3, treeRow: 5, folderParent: parentEntry);
        await database.SaveChangesAsync();

        var controller = new GrindingThunder.Api.Controllers.VehiclesController(database.Context);
        var response = Assert.IsType<OkObjectResult>(await controller.GetVehicles());
        var vehicles = Assert.IsAssignableFrom<System.Collections.IEnumerable>(response.Value!).Cast<object>().ToList();
        var parentDto = vehicles.Single(v => (Guid)GetProperty(v, "Id")! == parent.Id);
        var childDto = vehicles.Single(v => (Guid)GetProperty(v, "Id")! == child.Id);

        Assert.Equal(true, GetProperty(parentDto, "IsFolderParent"));
        Assert.Equal(parent.Id, GetProperty(childDto, "FolderParentId"));
        Assert.Equal(3, GetProperty(childDto, "TreeColumn"));
        Assert.Equal(5, GetProperty(childDto, "TreeRow"));

        var treeResponse = Assert.IsType<OkObjectResult>(
            await controller.GetVehicleTree(nation.Id, "Ground", CancellationToken.None));
        var treeEntries = Assert.IsAssignableFrom<System.Collections.IEnumerable>(treeResponse.Value!).Cast<object>().ToList();
        var childTreeEntry = treeEntries.Single(e => (Guid)GetProperty(e, "Id")! == child.Id);

        Assert.Equal(parent.Id, GetProperty(childTreeEntry, "FolderParentId"));
        Assert.Equal(3, GetProperty(childTreeEntry, "TreeColumn"));
        Assert.Equal(5, GetProperty(childTreeEntry, "TreeRow"));
    }

    [Fact]
    public void MigrationScript_CopiesLegacyLayoutAndFoldersBeforeDroppingVehicleColumns()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using var context = new ApplicationDbContext(options);
        var migrator = context.GetService<IMigrator>();

        var script = migrator.GenerateScript(
            "20261004083408_EnforceVehiclePrerequisiteVersionForeignKeys",
            "20261004084327_VersionVehicleLayoutAndFolders");

        var layoutCopy = script.IndexOf("UPDATE \"VehicleTreeEntries\" AS entry", StringComparison.Ordinal);
        var folderCopy = script.IndexOf("SET \"FolderParentEntryId\"", StringComparison.Ordinal);
        var oldColumnDrop = script.IndexOf("DROP COLUMN \"TreeRow\"", StringComparison.Ordinal);

        Assert.True(layoutCopy >= 0, "Migration must copy TreeRow and TreeColumn.");
        Assert.True(folderCopy >= 0, "Migration must map folder parents to same-version entries.");
        Assert.True(oldColumnDrop > layoutCopy && oldColumnDrop > folderCopy,
            "Legacy columns must be dropped only after their data is copied.");
        Assert.Contains("cannot be mapped to entries in the same research tree version", script);
    }

    private static object? GetProperty(object instance, string name) =>
        instance.GetType().GetProperty(name)?.GetValue(instance);
}
