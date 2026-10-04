using GrindingThunder.Api.Application.Models;
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
        var rank1 = database.AddRank(nation, 1);
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(rank1.ResearchTree, update);

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
        Assert.Equal(rank1.Id, persisted.RankId);
        Assert.Equal(2900, persisted.RpCost);
        Assert.Equal(700, persisted.SlCost);
    }

    [Fact]
    public async Task VehicleTreeEntry_DuplicateVehicleInSameVersion_IsRejected()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var rank = database.AddRank(nation, 1);
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(rank.ResearchTree, update);
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
        var rank = database.AddRank(nation, 1);
        var updateA = database.AddGameUpdate("2.43");
        var updateB = database.AddGameUpdate("2.45");
        var versionA = database.AddResearchTreeVersion(rank.ResearchTree, updateA);
        var versionB = database.AddResearchTreeVersion(rank.ResearchTree, updateB);
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
        var rank = database.AddRank(nation, 1);
        var orphan = new Vehicle { Id = Guid.NewGuid(), Name = "Orphan" };
        database.Context.Vehicles.Add(orphan);
        await database.SaveChangesAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            database.Calculator.CalculateResearchAsync(
                new ResearchCalculationRequest(
                    TargetVehicleId: orphan.Id,
                    AverageRpPerMatch: 1000,
                    UnlockedVehicleIds: [],
                    FillerTargetIds: [])));
    }

    [Fact]
    public async Task CalculateResearchAsync_TargetInMultipleVersions_FailsClearly()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var rank = database.AddRank(nation, 1);
        var updateA = database.AddGameUpdate("2.43");
        var updateB = database.AddGameUpdate("2.45");
        var versionA = database.AddResearchTreeVersion(rank.ResearchTree, updateA);
        var versionB = database.AddResearchTreeVersion(rank.ResearchTree, updateB);
        var a = database.AddVehicle(rank.ResearchTree, rank, "A", 10, treeVersion: versionA);
        var d = database.AddVehicle(rank.ResearchTree, rank, "D", 40, treeVersion: versionA);

        // The same target vehicle placed in a second version with a different cost.
        database.AddVehicleTreeEntry(versionB, d, rank, 99);
        await database.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Calculator.CalculateResearchAsync(
                new ResearchCalculationRequest(
                    TargetVehicleId: d.Id,
                    AverageRpPerMatch: 1000,
                    UnlockedVehicleIds: [],
                    FillerTargetIds: [])));
    }

    [Fact]
    public async Task CalculateResearchAsync_UsesEntryCostsForTheVehicleVersion()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var rank = database.AddRank(nation, 1);
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(rank.ResearchTree, update);
        var a = database.AddVehicle(rank.ResearchTree, rank, "A", 10, treeVersion: version);
        var b = database.AddVehicle(rank.ResearchTree, rank, "B", 20, treeVersion: version);
        database.AddPrerequisite(b, a);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            new ResearchCalculationRequest(
                TargetVehicleId: b.Id,
                AverageRpPerMatch: 1000,
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
    public async Task CalculateResearchAsync_NonTargetVehicleInMultipleVersions_FailsClearly()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var rank = database.AddRank(nation, 1);
        var updateA = database.AddGameUpdate("2.43");
        var updateB = database.AddGameUpdate("2.45");
        var versionA = database.AddResearchTreeVersion(rank.ResearchTree, updateA);
        var versionB = database.AddResearchTreeVersion(rank.ResearchTree, updateB);
        var target = database.AddVehicle(rank.ResearchTree, rank, "Target", 40, treeVersion: versionA);

        // A NON-target vehicle spans two versions; the target itself does not.
        var ambiguous = database.AddVehicle(rank.ResearchTree, rank, "Ambiguous", 10, treeVersion: versionA);
        database.AddVehicleTreeEntry(versionB, ambiguous, rank, 99);
        database.AddPrerequisite(target, ambiguous);
        await database.SaveChangesAsync();

        // Must fail with the deliberate domain error, not an opaque
        // ArgumentException from ToDictionary.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Calculator.CalculateResearchAsync(
                new ResearchCalculationRequest(
                    TargetVehicleId: target.Id,
                    AverageRpPerMatch: 1000,
                    UnlockedVehicleIds: [],
                    FillerTargetIds: [])));

        Assert.Contains(ambiguous.Id.ToString(), exception.Message);
        Assert.Contains("multiple research tree versions", exception.Message);
    }
}

public class VehiclesControllerTests
{
    [Fact]
    public async Task GetVehicles_EntrylessVehicle_DoesNotThrowAndReturnsNullCosts()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var rank = database.AddRank(nation, 1);
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(rank.ResearchTree, update);
        var placed = database.AddVehicle(rank.ResearchTree, rank, "Placed", 10, treeVersion: version);

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
        var rank = database.AddRank(nation, 1);
        var updateA = database.AddGameUpdate("2.43");
        var updateB = database.AddGameUpdate("2.45");
        var versionA = database.AddResearchTreeVersion(rank.ResearchTree, updateA);
        var versionB = database.AddResearchTreeVersion(rank.ResearchTree, updateB);

        var vehicle = database.AddVehicle(rank.ResearchTree, rank, "Ambiguous", 10, treeVersion: versionA);
        database.AddVehicleTreeEntry(versionB, vehicle, rank, 99);
        await database.SaveChangesAsync();

        var controller = new GrindingThunder.Api.Controllers.VehiclesController(database.Context);
        var actionResult = await controller.GetVehicles();

        var ok = Assert.IsType<OkObjectResult>(actionResult);
        var body = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ok.Value!).Cast<object>().ToList();
        Assert.Equal(1, body.Count);
        var dto = body[0];

        // No arbitrary version picked as canonical; flat values are null.
        Assert.Null(GetProp(dto, "RankId"));
        Assert.Null(GetProp(dto, "RpCost"));
        Assert.Null(GetProp(dto, "SlCost"));

        // The full, honest list of entries is still exposed.
        var entries = Assert.IsAssignableFrom<System.Collections.IEnumerable>(GetProp(dto, "TreeEntries"));
        Assert.Equal(2, entries.Cast<object>().Count());
    }

    private static object? GetProp(object obj, string name) =>
        obj.GetType().GetProperty(name)?.GetValue(obj);
}
