using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Application.Exceptions;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using GrindingThunder.Api.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class VehicleTreeEntryTests
{
    [Fact]
    public async Task VehicleTreeEntry_StoresVersionSpecificRankAndCosts()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(
            database.AddResearchTree(nation, database.AddVehicleType("Ground")), update);
        var rank1 = database.AddTreeRank(version, 1);

        // One stable vehicle identity with version-specific rank and costs.
        var vehicle = new Vehicle { Id = Guid.NewGuid(), Name = "Test Tank" };
        database.Context.Vehicles.Add(vehicle);
        database.AddVehicleTreeEntry(version, vehicle, rank1, rpCost: 2900, slCost: 700);

        await database.SaveChangesAsync();
        database.Context.ChangeTracker.Clear();

        var persisted = await database.Context.VehicleTreeEntries
            .AsNoTracking()
            .SingleAsync(e => e.VehicleId == vehicle.Id);

        Assert.Equal(version.Id, persisted.ResearchTreeVersionId);
        Assert.Equal(rank1.Id, persisted.TreeRankId);
        Assert.Equal(2900, persisted.RpCost);
        Assert.Equal(700, persisted.SlCost);
    }

    [Fact]
    public async Task VehicleTreeEntry_DuplicateVehicleInSameVersion_IsRejected()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(
            database.AddResearchTree(nation, database.AddVehicleType("Ground")), update);
        var rank = database.AddTreeRank(version, 1);
        var vehicle = new Vehicle { Id = Guid.NewGuid(), Name = "Test Tank" };
        database.Context.Vehicles.Add(vehicle);

        database.AddVehicleTreeEntry(version, vehicle, rank, 2900);
        database.AddVehicleTreeEntry(version, vehicle, rank, 4000);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => database.SaveChangesAsync());
    }

    [Fact]
    public async Task VehicleTreeEntry_SameVehicleInDifferentVersions_IsAllowed()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var rank = database.AddTreeRank(nation, 1);
        var updateA = database.AddGameUpdate("2.43");
        var updateB = database.AddGameUpdate("2.45");
        var versionA = database.AddResearchTreeVersion(rank.ResearchTreeVersion.ResearchTree, updateA);
        var versionB = database.AddResearchTreeVersion(rank.ResearchTreeVersion.ResearchTree, updateB);
        var vehicle = new Vehicle { Id = Guid.NewGuid(), Name = "Test Tank" };
        database.Context.Vehicles.Add(vehicle);

        // Same vehicle, different versions, different RP costs.
        database.AddVehicleTreeEntry(versionA, vehicle, rank, rpCost: 2900, slCost: 700);
        database.AddVehicleTreeEntry(versionB, vehicle, rank, rpCost: 4000, slCost: 1400);

        await database.SaveChangesAsync();

        var entries = await database.Context.VehicleTreeEntries
            .AsNoTracking()
            .Where(e => e.VehicleId == vehicle.Id)
            .ToListAsync();

        Assert.Equal(2, entries.Count);
        Assert.Equal(new[] { 2900, 4000 }, entries.Select(e => e.RpCost).OrderBy(c => c));
    }
}


public class VehicleTreeEntryCalculatorTests
{
    [Fact]
    public async Task CalculateResearchAsync_VehicleWithoutTreeEntry_Fails()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var rank = database.AddTreeRank(nation, 1);
        var orphan = new Vehicle { Id = Guid.NewGuid(), Name = "Orphan" };
        database.Context.Vehicles.Add(orphan);
        await database.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ResearchCalculationInputException>(() =>
            database.Calculator.CalculateResearchAsync(
                new ResearchCalculationRequest(
                    ResearchTreeVersionId: rank.ResearchTreeVersionId,
                    TargetVehicleId: orphan.Id,
                    AverageRpPerMatch: 1000,
                    AverageNetSlPerMatch: int.MaxValue,
                    UnlockedVehicleIds: [],
                    FillerTargetIds: [])));

        Assert.Contains(orphan.Id.ToString(), exception.Message);
        Assert.Contains(rank.ResearchTreeVersionId.ToString(), exception.Message);
    }

    [Fact]
    public async Task CalculateResearchAsync_TargetInMultipleVersions_UsesSelectedVersionCost()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var rank = database.AddTreeRank(nation, 1);
        var updateA = database.AddGameUpdate("2.43");
        var updateB = database.AddGameUpdate("2.45");
        var versionA = database.AddResearchTreeVersion(rank.ResearchTreeVersion.ResearchTree, updateA);
        var versionB = database.AddResearchTreeVersion(rank.ResearchTreeVersion.ResearchTree, updateB);
        var a = database.AddVehicle(rank.ResearchTreeVersion.ResearchTree, rank, "A", 10, treeVersion: versionA);
        var d = database.AddVehicle(
            rank.ResearchTreeVersion.ResearchTree,
            rank,
            "D",
            40,
            treeVersion: versionA,
            slCost: 400);

        // The same target vehicle placed in a second version with different costs.
        database.AddVehicleTreeEntry(versionB, d, rank, rpCost: 99, slCost: 990);
        await database.SaveChangesAsync();

        var resultA = await database.Calculator.CalculateResearchAsync(
            new ResearchCalculationRequest(versionA.Id, d.Id, [], [], 1000, int.MaxValue));
        var resultB = await database.Calculator.CalculateResearchAsync(
            new ResearchCalculationRequest(versionB.Id, d.Id, [], [], 1000, int.MaxValue));

        Assert.Equal(40, resultA.TotalRpRequired);
        Assert.Equal(400, resultA.TotalSlRequired);
        Assert.Equal(99, resultB.TotalRpRequired);
        Assert.Equal(990, resultB.TotalSlRequired);
    }

    [Fact]
    public async Task CalculateResearchAsync_UsesEntryCostsForTheVehicleVersion()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(
            database.AddResearchTree(nation, database.AddVehicleType("Ground")), update);
        var rank = database.AddTreeRank(version, 1);
        var a = database.AddVehicle(rank.ResearchTreeVersion.ResearchTree, rank, "A", 10, treeVersion: version);
        var b = database.AddVehicle(rank.ResearchTreeVersion.ResearchTree, rank, "B", 20, treeVersion: version);
        database.AddPrerequisite(b, a);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            new ResearchCalculationRequest(
                ResearchTreeVersionId: version.Id,
                TargetVehicleId: b.Id,
                AverageRpPerMatch: 1000,
                AverageNetSlPerMatch: int.MaxValue,
                UnlockedVehicleIds: [],
                FillerTargetIds: []));

        Assert.Equal(30, result.TotalRpRequired);
    }

    [Fact]
    public async Task DbInitializer_SeedsVehiclesWithTreeEntries()
    {
        using var database = new CalculatorTestDatabase();

        await DbInitializer.InitializeAsync(database.Context);

        var entries = await database.Context.VehicleTreeEntries
            .AsNoTracking()
            .Include(e => e.Vehicle)
            .Include(e => e.ResearchTreeVersion)
            .ToListAsync();

        Assert.Equal(6, entries.Count);
        Assert.All(entries, entry =>
        {
            Assert.Equal(ResearchTreeVersionStatus.Published, entry.ResearchTreeVersion.Status);
            Assert.False(string.IsNullOrWhiteSpace(entry.Vehicle.Name));
            Assert.NotEqual(0, entry.RpCost);
        });
        Assert.Contains(entries, e => e.Vehicle.Name == "M4A1 Sherman" && e.RpCost == 9200);
        Assert.Contains(entries, e => e.Vehicle.Name == "M2 Light" && e.RpCost == 2900);
    }

    [Fact]
    public async Task CalculateResearchAsync_NonTargetVehicleInMultipleVersions_UsesSelectedVersionOnly()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var rank = database.AddTreeRank(nation, 1);
        var updateA = database.AddGameUpdate("2.43");
        var updateB = database.AddGameUpdate("2.45");
        var versionA = database.AddResearchTreeVersion(rank.ResearchTreeVersion.ResearchTree, updateA);
        var versionB = database.AddResearchTreeVersion(rank.ResearchTreeVersion.ResearchTree, updateB);
        var target = database.AddVehicle(rank.ResearchTreeVersion.ResearchTree, rank, "Target", 40, treeVersion: versionA);

        // A NON-target vehicle spans two versions; the target itself does not.
        var ambiguous = database.AddVehicle(rank.ResearchTreeVersion.ResearchTree, rank, "Ambiguous", 10, treeVersion: versionA);
        database.AddVehicleTreeEntry(versionB, ambiguous, rank, 99);
        database.AddPrerequisite(target, ambiguous, versionA);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            new ResearchCalculationRequest(versionA.Id, target.Id, [], [], 1000, int.MaxValue));

        Assert.Equal(50, result.TotalRpRequired);
        Assert.Contains(result.RequiredVehicles, vehicle =>
            vehicle.VehicleId == ambiguous.Id && vehicle.RpRemaining == 10);
    }
}

public class VehiclesControllerTests
{
    [Fact]
    public async Task GetVehicles_EntrylessVehicle_DoesNotThrowAndReturnsNullCosts()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(
            database.AddResearchTree(nation, database.AddVehicleType("Ground")), update);
        var rank = database.AddTreeRank(version, 1);
        var placed = database.AddVehicle(rank.ResearchTreeVersion.ResearchTree, rank, "Placed", 10, treeVersion: version);

        // Entry-less vehicle: previously TreeEntries[0] would throw.
        var orphan = new Vehicle { Id = Guid.NewGuid(), Name = "Orphan" };
        database.Context.Vehicles.Add(orphan);
        await database.SaveChangesAsync();

        var controller = new GrindingThunder.Api.Controllers.VehiclesController(database.Context);
        var actionResult = await controller.GetVehicles();

        var ok = Assert.IsType<OkObjectResult>(actionResult);
        var body = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ok.Value!).Cast<object>().ToList();
        Assert.Equal(2, body.Count);

        var placedDto = body.Single(dto => (Guid)GetProp(dto, "Id")! == placed.Id);
        var orphanDto = body.Single(dto => (Guid)GetProp(dto, "Id")! == orphan.Id);

        // Single-entry vehicle keeps its flat values populated.
        Assert.Equal(rank.Id, (Guid)GetProp(placedDto, "RankId")!);
        Assert.Equal(10, (int)GetProp(placedDto, "RpCost")!);

        // Orphan has no entry: flat values must be null, not a crash.
        Assert.Null(GetProp(orphanDto, "RankId"));
        Assert.Null(GetProp(orphanDto, "RpCost"));
        Assert.Null(GetProp(orphanDto, "SlCost"));
        var orphanEntries = Assert.IsAssignableFrom<System.Collections.IEnumerable>(GetProp(orphanDto, "TreeEntries"));
        Assert.Empty(orphanEntries.Cast<object>());
    }

    [Fact]
    public async Task GetVehicles_MultiVersionVehicle_ReturnsNullFlatCostsAndAllEntries()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var rank = database.AddTreeRank(nation, 1);
        var updateA = database.AddGameUpdate("2.43");
        var updateB = database.AddGameUpdate("2.45");
        var versionA = database.AddResearchTreeVersion(rank.ResearchTreeVersion.ResearchTree, updateA);
        var versionB = database.AddResearchTreeVersion(rank.ResearchTreeVersion.ResearchTree, updateB);

        var vehicle = database.AddVehicle(rank.ResearchTreeVersion.ResearchTree, rank, "Ambiguous", 10, treeVersion: versionA);
        database.AddVehicleTreeEntry(versionB, vehicle, rank, 99);
        await database.SaveChangesAsync();

        var controller = new GrindingThunder.Api.Controllers.VehiclesController(database.Context);
        var actionResult = await controller.GetVehicles();

        var ok = Assert.IsType<OkObjectResult>(actionResult);
        var body = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ok.Value!).Cast<object>().ToList();
        Assert.Single(body);
        var dto = body[0];

        // No arbitrary version picked as canonical; flat values are null.
        Assert.Null(GetProp(dto, "RankId"));
        Assert.Null(GetProp(dto, "RpCost"));
        Assert.Null(GetProp(dto, "SlCost"));
        Assert.Null(GetProp(dto, "TreeColumn"));
        Assert.Null(GetProp(dto, "TreeRow"));
        Assert.Null(GetProp(dto, "FolderParentId"));
        Assert.Null(GetProp(dto, "IsFolderParent"));

        // The full, honest list of entries is still exposed.
        var entries = Assert.IsAssignableFrom<System.Collections.IEnumerable>(GetProp(dto, "TreeEntries"));
        Assert.Equal(2, entries.Cast<object>().Count());
    }

    private static object? GetProp(object obj, string name) =>
        obj.GetType().GetProperty(name)?.GetValue(obj);
}
