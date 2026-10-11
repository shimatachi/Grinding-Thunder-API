using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Application.Exceptions;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class ResearchCalculatorServiceTests
{
    [Fact]
    public async Task CalculateResearchAsync_ExistingSeedData_PreservesBaselineResult()
    {
        using var database = new CalculatorTestDatabase();
        await DbInitializer.InitializeAsync(database.Context);

        var target = await database.Context.Vehicles
            .SingleAsync(vehicle => vehicle.Name == "M4A1 Sherman");

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(target, averageRpPerMatch: 1_000));

        // Mandatory line: M2 Light (2 900), M3 Stuart (4 000), M4A1 Sherman (9 200).
        // Rank 1 threshold is 5. Mandatory covers 2 vehicles; auto-selection adds
        // M2A4 (2 900 RP, cheapest) and M3A1 Stuart (4 000 RP, next cheapest).
        // That brings the count to 4 — one short of 5 — so a deficit of 1 remains.
        Assert.Equal(23_000, result.TotalRpRequired);
        Assert.Equal(23, result.EstimatedMatches);
        Assert.Equal(
            new[] { "M2 Light", "M2A4", "M3 Stuart", "M3A1 Stuart", "M4A1 Sherman" }.OrderBy(name => name),
            result.RequiredVehicles.Select(vehicle => vehicle.Name).OrderBy(name => name));

        // Auto-selected vehicles are flagged as rank-gate fillers.
        var autoFillerNames = result.RequiredVehicles
            .Where(v => v.IsRankGateFiller)
            .Select(v => v.Name)
            .OrderBy(name => name)
            .ToList();
        Assert.Equal(new[] { "M2A4", "M3A1 Stuart" }, autoFillerNames);

        // One remaining deficit: Rank 1 still needs 1 more vehicle after auto-selection.
        var deficit = Assert.Single(result.RankDeficits);
        Assert.Equal(1, deficit.RankNumber);
        Assert.Equal(1, deficit.Shortfall);
        Assert.Equal(5, deficit.Required);
    }

    [Fact]
    public async Task CalculateResearchAsync_FillerFromOtherTreeForSameNation_IsRejected()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var ground = database.AddVehicleType("Ground");
        var aviation = database.AddVehicleType("Aviation");
        var groundTree = database.AddResearchTree(nation, ground);
        var aviationTree = database.AddResearchTree(nation, aviation);
        var groundRankOne = database.AddTreeRank(groundTree, 1, requiredVehiclesUnlocked: 2);
        var groundRankTwo = database.AddTreeRank(groundTree, 2);
        var aviationRankOne = database.AddTreeRank(aviationTree, 1);
        var mandatory = database.AddVehicle(groundRankOne, "Ground prerequisite", 10);
        var target = database.AddVehicle(groundRankTwo, "Ground target", 20);
        var aviationFiller = database.AddVehicle(aviationRankOne, "Aviation filler", 30);
        database.AddPrerequisite(target, mandatory);
        await database.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ResearchCalculationInputException>(() =>
            database.Calculator.CalculateResearchAsync(
                RequestFor(target, fillerTargetIds: [aviationFiller.Id])));

        Assert.Contains("Filler target IDs", exception.Message);
        Assert.Contains(aviationFiller.Id.ToString(), exception.Message);
    }

    [Fact]
    public async Task CalculateResearchAsync_LinearPrerequisiteChain_IncludesEveryVehicleOnce()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rank = database.AddTreeRank(nation, 1);
        var a = database.AddVehicle(rank, "A", 10, 100);
        var b = database.AddVehicle(rank, "B", 20, 200);
        var c = database.AddVehicle(rank, "C", 30, 300);
        var d = database.AddVehicle(rank, "D", 40, 400);
        database.AddPrerequisite(b, a);
        database.AddPrerequisite(c, b);
        database.AddPrerequisite(d, c);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(RequestFor(d));

        AssertVehicleIds(result, d, c, b, a);
        Assert.Equal(100, result.TotalRpRequired);
    }

    [Fact]
    public async Task CalculateResearchAsync_OwnedVehicleInMandatoryLine_StopsTraversalAtOwnedBoundary()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rank = database.AddTreeRank(nation, 1);
        var a = database.AddVehicle(rank, "A", 10, 100);
        var b = database.AddVehicle(rank, "B", 20, 200);
        var c = database.AddVehicle(rank, "C", 30, 300);
        var d = database.AddVehicle(rank, "D", 40, 400);
        database.AddPrerequisite(b, a);
        database.AddPrerequisite(c, b);
        database.AddPrerequisite(d, c);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(d, unlockedVehicleIds: [c.Id]));

        AssertVehicleIds(result, d);
        Assert.Equal(40, result.TotalRpRequired);
        Assert.Equal(400, result.TotalSlRequired);
    }

    [Fact]
    public async Task CalculateResearchAsync_SharedPrerequisite_IncludesSharedVehicleOnce()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rank = database.AddTreeRank(nation, 1);
        var a = database.AddVehicle(rank, "A", 10, 100);
        var b = database.AddVehicle(rank, "B", 20, 200);
        var c = database.AddVehicle(rank, "C", 30, 300);
        var d = database.AddVehicle(rank, "D", 40, 400);
        database.AddPrerequisite(b, a);
        database.AddPrerequisite(c, a);
        database.AddPrerequisite(d, b);
        database.AddPrerequisite(d, c);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(RequestFor(d));

        AssertVehicleIds(result, d, b, c, a);
        Assert.Single(result.RequiredVehicles, vehicle => vehicle.VehicleId == a.Id);
        Assert.Equal(100, result.TotalRpRequired);
        Assert.Equal(1_000, result.TotalSlRequired);
    }

    [Fact]
    public async Task CalculateResearchAsync_CyclicPrerequisites_TerminatesAndIncludesEachVehicleOnce()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rank = database.AddTreeRank(nation, 1);
        var a = database.AddVehicle(rank, "A", 10);
        var b = database.AddVehicle(rank, "B", 20);
        database.AddPrerequisite(a, b);
        database.AddPrerequisite(b, a);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(RequestFor(b));

        AssertVehicleIds(result, b, a);
        Assert.Equal(30, result.TotalRpRequired);
    }

    [Fact]
    public async Task CalculateResearchAsync_CyclicFillerPrerequisites_TerminatesAndIncludesEachVehicleOnce()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rank = database.AddTreeRank(nation, 1);
        var target = database.AddVehicle(rank, "Target", 10);
        var fillerA = database.AddVehicle(rank, "Filler A", 20);
        var fillerB = database.AddVehicle(rank, "Filler B", 30);
        database.AddPrerequisite(fillerA, fillerB);
        database.AddPrerequisite(fillerB, fillerA);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(target, fillerTargetIds: [fillerA.Id]));

        AssertVehicleIds(result, target, fillerA, fillerB);
        Assert.Equal(60, result.TotalRpRequired);
        Assert.Equal(2, result.RequiredVehicles.Count(vehicle => vehicle.IsRankGateFiller));
    }

    [Fact]
    public async Task CalculateResearchAsync_ExplicitFillerChain_IncludesTargetAndPrerequisitesAsFillers()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rank = database.AddTreeRank(nation, 1);
        var c = database.AddVehicle(rank, "C", 30, 300);
        var d = database.AddVehicle(rank, "D", 40, 400);
        var e = database.AddVehicle(rank, "E", 50, 500);
        var f = database.AddVehicle(rank, "F", 60, 600);
        database.AddPrerequisite(d, c);
        database.AddPrerequisite(f, e);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(d, fillerTargetIds: [f.Id]));

        AssertVehicleIds(result, d, c, f, e);
        Assert.Equal(
            new[] { f.Id, e.Id }.OrderBy(id => id),
            result.RequiredVehicles
                .Where(vehicle => vehicle.IsRankGateFiller)
                .Select(vehicle => vehicle.VehicleId)
                .OrderBy(id => id));
        Assert.Equal(180, result.TotalRpRequired);
        Assert.Equal(1_800, result.TotalSlRequired);
        Assert.Equal(4, result.RequiredVehicles.Count);
    }

    [Fact]
    public async Task CalculateResearchAsync_OwnedFillerPrerequisite_StopsFillerTraversal()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rank = database.AddTreeRank(nation, 1);
        var c = database.AddVehicle(rank, "C", 30);
        var d = database.AddVehicle(rank, "D", 40);
        var e = database.AddVehicle(rank, "E", 50);
        var f = database.AddVehicle(rank, "F", 60);
        database.AddPrerequisite(d, c);
        database.AddPrerequisite(f, e);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(d, unlockedVehicleIds: [e.Id], fillerTargetIds: [f.Id]));

        AssertVehicleIds(result, d, c, f);
        var filler = Assert.Single(result.RequiredVehicles, vehicle => vehicle.IsRankGateFiller);
        Assert.Equal(f.Id, filler.VehicleId);
    }

    [Fact]
    public async Task CalculateResearchAsync_FillerOverlappingMandatoryLine_IsNotDuplicatedOrMarkedAsFiller()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rank = database.AddTreeRank(nation, 1);
        var c = database.AddVehicle(rank, "C", 30);
        var d = database.AddVehicle(rank, "D", 40);
        database.AddPrerequisite(d, c);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(d, fillerTargetIds: [c.Id]));

        AssertVehicleIds(result, d, c);
        var cSummary = Assert.Single(result.RequiredVehicles, vehicle => vehicle.VehicleId == c.Id);
        Assert.False(cSummary.IsRankGateFiller);
    }

    [Fact]
    public async Task CalculateResearchAsync_SatisfiedLowerRankGates_ReturnsNoDeficits()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rankOne = database.AddTreeRank(nation, 1, requiredVehiclesUnlocked: 2);
        var rankTwo = database.AddTreeRank(nation, 2, requiredVehiclesUnlocked: 2);
        var rankThree = database.AddTreeRank(nation, 3);
        var mandatoryOne = database.AddVehicle(rankOne, "Mandatory I", 10);
        var ownedOne = database.AddVehicle(rankOne, "Owned I", 10);
        var mandatoryTwo = database.AddVehicle(rankTwo, "Mandatory II", 20);
        var ownedTwo = database.AddVehicle(rankTwo, "Owned II", 20);
        var target = database.AddVehicle(rankThree, "Target", 30);
        database.AddPrerequisite(target, mandatoryTwo);
        database.AddPrerequisite(mandatoryTwo, mandatoryOne);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(target, unlockedVehicleIds: [ownedOne.Id, ownedTwo.Id]));

        Assert.Empty(result.RankDeficits);
    }

    [Fact]
    public async Task CalculateResearchAsync_MultipleRanksWithNoAutoSelectCandidates_ReportsAllDeficits()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rankOne = database.AddTreeRank(nation, 1, requiredVehiclesUnlocked: 1);
        var rankTwo = database.AddTreeRank(nation, 2, requiredVehiclesUnlocked: 2);
        var rankThree = database.AddTreeRank(nation, 3, requiredVehiclesUnlocked: 2);
        var rankFour = database.AddTreeRank(nation, 4);
        var ownedOne = database.AddVehicle(rankOne, "Owned I", 10);
        var mandatoryTwo = database.AddVehicle(rankTwo, "Mandatory II", 20);
        var mandatoryThree = database.AddVehicle(rankThree, "Mandatory III", 30);
        var target = database.AddVehicle(rankFour, "Target", 40);
        database.AddPrerequisite(target, mandatoryThree);
        database.AddPrerequisite(mandatoryThree, mandatoryTwo);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(target, unlockedVehicleIds: [ownedOne.Id]));

        // Rank 1 is satisfied (ownedOne counts). Ranks 2 and 3 each have only
        // one vehicle which is already in the mandatory line — no auto-select
        // candidates exist — so both report a shortfall of 1.
        Assert.Equal(2, result.RankDeficits.Count);
        var deficitTwo = result.RankDeficits.Single(d => d.RankNumber == 2);
        Assert.Equal(1, deficitTwo.Shortfall);
        Assert.Equal(2, deficitTwo.Required);
        var deficitThree = result.RankDeficits.Single(d => d.RankNumber == 3);
        Assert.Equal(1, deficitThree.Shortfall);
        Assert.Equal(2, deficitThree.Required);
    }

    [Fact]
    public async Task CalculateResearchAsync_ExplicitFillerInDeficitRank_SatisfiesGate()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        database.AddTreeRank(nation, 1);
        var rankTwo = database.AddTreeRank(nation, 2, requiredVehiclesUnlocked: 2);
        var rankThree = database.AddTreeRank(nation, 3);
        var mandatory = database.AddVehicle(rankTwo, "Mandatory", 20);
        var filler = database.AddVehicle(rankTwo, "Filler", 25);
        var target = database.AddVehicle(rankThree, "Target", 30);
        database.AddPrerequisite(target, mandatory);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(target, fillerTargetIds: [filler.Id]));

        Assert.Empty(result.RankDeficits);
        Assert.Contains(
            result.RequiredVehicles,
            vehicle => vehicle.VehicleId == filler.Id && vehicle.IsRankGateFiller);
    }

    [Fact]
    public async Task CalculateResearchAsync_NonRoundRpDivision_RoundsEstimatedMatchesUp()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rank = database.AddTreeRank(nation, 1);
        var target = database.AddVehicle(rank, "Target", 100);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(target, averageRpPerMatch: 30));

        Assert.Equal(100, result.TotalRpRequired);
        Assert.Equal(4, result.RpEstimatedMatches);
        Assert.Equal(0, result.SlEstimatedMatches);
        Assert.Equal(4, result.EstimatedMatches);
    }

    [Fact]
    public async Task CalculateResearchAsync_SlDivision_RoundsUpAndZeroRpProducesZeroRpMatches()
    {
        using var database = new CalculatorTestDatabase();
        var rank = database.AddTreeRank(database.AddNation(), 1);
        var target = database.AddVehicle(rank, "Target", rpCost: 0, slCost: 25_000);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(
                target,
                averageRpPerMatch: 2_000,
                averageNetSlPerMatch: 10_000));

        Assert.Equal(0, result.TotalRpRequired);
        Assert.Equal(25_000, result.TotalSlRequired);
        Assert.Equal(0, result.RpEstimatedMatches);
        Assert.Equal(3, result.SlEstimatedMatches);
        Assert.Equal(3, result.EstimatedMatches);
    }

    [Fact]
    public async Task CalculateResearchAsync_RpIsLimitingResource_UsesRpEstimate()
    {
        using var database = new CalculatorTestDatabase();
        var rank = database.AddTreeRank(database.AddNation(), 1);
        var target = database.AddVehicle(rank, "Target", rpCost: 8_000, slCost: 5_000);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(target, averageRpPerMatch: 1_000, averageNetSlPerMatch: 1_000));

        Assert.Equal(8, result.RpEstimatedMatches);
        Assert.Equal(5, result.SlEstimatedMatches);
        Assert.Equal(8, result.EstimatedMatches);
    }

    [Fact]
    public async Task CalculateResearchAsync_SlIsLimitingResource_UsesSlEstimate()
    {
        using var database = new CalculatorTestDatabase();
        var rank = database.AddTreeRank(database.AddNation(), 1);
        var target = database.AddVehicle(rank, "Target", rpCost: 4_000, slCost: 9_000);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(target, averageRpPerMatch: 1_000, averageNetSlPerMatch: 1_000));

        Assert.Equal(4, result.RpEstimatedMatches);
        Assert.Equal(9, result.SlEstimatedMatches);
        Assert.Equal(9, result.EstimatedMatches);
    }

    [Fact]
    public async Task CalculateResearchAsync_EqualResourceEstimates_UsesThatEstimate()
    {
        using var database = new CalculatorTestDatabase();
        var rank = database.AddTreeRank(database.AddNation(), 1);
        var target = database.AddVehicle(rank, "Target", rpCost: 4_000, slCost: 10_000);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(target, averageRpPerMatch: 1_000, averageNetSlPerMatch: 2_500));

        Assert.Equal(4, result.RpEstimatedMatches);
        Assert.Equal(4, result.SlEstimatedMatches);
        Assert.Equal(4, result.EstimatedMatches);
    }

    [Fact]
    public async Task CalculateResearchAsync_ZeroSlCost_ProducesZeroSlMatches()
    {
        using var database = new CalculatorTestDatabase();
        var rank = database.AddTreeRank(database.AddNation(), 1);
        var target = database.AddVehicle(rank, "Target", rpCost: 5_000, slCost: 0);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(target, averageRpPerMatch: 2_000, averageNetSlPerMatch: 10_000));

        Assert.Equal(3, result.RpEstimatedMatches);
        Assert.Equal(0, result.SlEstimatedMatches);
        Assert.Equal(3, result.EstimatedMatches);
    }

    [Fact]
    public async Task CalculateResearchAsync_DifferentPrerequisitesAcrossVersions_UsesSelectedGraphOnly()
    {
        using var database = new CalculatorTestDatabase();
        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var versionA = database.AddResearchTreeVersion(tree, database.AddGameUpdate("2.43"));
        var versionB = database.AddResearchTreeVersion(tree, database.AddGameUpdate("2.45"));
        var rankA = database.AddTreeRank(versionA, 1);
        var rankB = database.AddTreeRank(versionB, 1);
        var prerequisiteA = database.AddVehicle(tree, rankA, "Prerequisite A", 10, versionA);
        var prerequisiteB = database.AddVehicle(tree, rankB, "Prerequisite B", 20, versionB);
        var target = database.AddVehicle(tree, rankA, "Target", 100, versionA);
        database.AddVehicleTreeEntry(versionB, target, rankB, 200);
        database.AddPrerequisite(target, prerequisiteA, versionA);
        database.AddPrerequisite(target, prerequisiteB, versionB);
        await database.SaveChangesAsync();

        var resultA = await database.Calculator.CalculateResearchAsync(
            new ResearchCalculationRequest(versionA.Id, target.Id, [], [], 100, int.MaxValue));
        var resultB = await database.Calculator.CalculateResearchAsync(
            new ResearchCalculationRequest(versionB.Id, target.Id, [], [], 100, int.MaxValue));

        Assert.Equal(110, resultA.TotalRpRequired);
        Assert.Equal(new[] { target.Id, prerequisiteA.Id }.OrderBy(id => id),
            resultA.RequiredVehicles.Select(vehicle => vehicle.VehicleId).OrderBy(id => id));
        Assert.Equal(220, resultB.TotalRpRequired);
        Assert.Equal(new[] { target.Id, prerequisiteB.Id }.OrderBy(id => id),
            resultB.RequiredVehicles.Select(vehicle => vehicle.VehicleId).OrderBy(id => id));
    }

    [Fact]
    public async Task CalculateResearchAsync_DifferentRankGatesAcrossVersions_UsesSelectedGateOnly()
    {
        using var database = new CalculatorTestDatabase();
        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var versionA = database.AddResearchTreeVersion(tree, database.AddGameUpdate("2.43"));
        var versionB = database.AddResearchTreeVersion(tree, database.AddGameUpdate("2.45"));
        database.AddTreeRank(versionA, 1, requiredVehiclesUnlocked: 1);
        var targetRankA = database.AddTreeRank(versionA, 2);
        database.AddTreeRank(versionB, 1, requiredVehiclesUnlocked: 2);
        var targetRankB = database.AddTreeRank(versionB, 2);
        var target = database.AddVehicle(tree, targetRankA, "Target", 100, versionA);
        database.AddVehicleTreeEntry(versionB, target, targetRankB, 100);
        await database.SaveChangesAsync();

        var resultA = await database.Calculator.CalculateResearchAsync(
            new ResearchCalculationRequest(versionA.Id, target.Id, [], [], 100, int.MaxValue));
        var resultB = await database.Calculator.CalculateResearchAsync(
            new ResearchCalculationRequest(versionB.Id, target.Id, [], [], 100, int.MaxValue));

        var deficitA = Assert.Single(resultA.RankDeficits);
        Assert.Equal(1, deficitA.Required);
        Assert.Equal(1, deficitA.Shortfall);
        var deficitB = Assert.Single(resultB.RankDeficits);
        Assert.Equal(2, deficitB.Required);
        Assert.Equal(2, deficitB.Shortfall);
    }

    [Fact]
    public async Task CalculateResearchAsync_TargetOnlyInAnotherVersion_FailsClearly()
    {
        using var database = new CalculatorTestDatabase();
        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var versionA = database.AddResearchTreeVersion(tree, database.AddGameUpdate("2.43"));
        var versionB = database.AddResearchTreeVersion(tree, database.AddGameUpdate("2.45"));
        database.AddTreeRank(versionA, 1);
        var rankB = database.AddTreeRank(versionB, 1);
        var target = database.AddVehicle(tree, rankB, "Target", 100, versionB);
        await database.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ResearchCalculationInputException>(() =>
            database.Calculator.CalculateResearchAsync(
                new ResearchCalculationRequest(versionA.Id, target.Id, [], [], 100, int.MaxValue)));

        Assert.Contains("Target vehicle", exception.Message);
        Assert.Contains(target.Id.ToString(), exception.Message);
        Assert.Contains(versionA.Id.ToString(), exception.Message);
    }

    [Fact]
    public async Task CalculateResearchAsync_UnknownVersion_FailsClearly()
    {
        using var database = new CalculatorTestDatabase();
        var unknownVersionId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<ResearchTreeVersionNotFoundException>(() =>
            database.Calculator.CalculateResearchAsync(
                new ResearchCalculationRequest(unknownVersionId, Guid.NewGuid(), [], [], 100, int.MaxValue)));

        Assert.Contains(unknownVersionId.ToString(), exception.Message);
    }

    [Fact]
    public async Task CalculateResearchAsync_OwnedVehicleOnlyInAnotherVersion_IsRejected()
    {
        using var database = new CalculatorTestDatabase();
        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var versionA = database.AddResearchTreeVersion(tree, database.AddGameUpdate("2.43"));
        var versionB = database.AddResearchTreeVersion(tree, database.AddGameUpdate("2.45"));
        var rankA = database.AddTreeRank(versionA, 1);
        var rankB = database.AddTreeRank(versionB, 1);
        var target = database.AddVehicle(tree, rankA, "Target", 100, versionA);
        var otherVersionVehicle = database.AddVehicle(tree, rankB, "Other version", 10, versionB);
        await database.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ResearchCalculationInputException>(() =>
            database.Calculator.CalculateResearchAsync(
                new ResearchCalculationRequest(
                    versionA.Id,
                    target.Id,
                    [otherVersionVehicle.Id],
                    [],
                    100,
                    int.MaxValue)));

        Assert.Contains("Unlocked vehicle IDs", exception.Message);
        Assert.Contains(otherVersionVehicle.Id.ToString(), exception.Message);
        Assert.Contains(versionA.Id.ToString(), exception.Message);
    }

    [Fact]
    public async Task CalculateResearchAsync_FillerOnlyInAnotherVersion_IsRejected()
    {
        using var database = new CalculatorTestDatabase();
        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var versionA = database.AddResearchTreeVersion(tree, database.AddGameUpdate("2.43"));
        var versionB = database.AddResearchTreeVersion(tree, database.AddGameUpdate("2.45"));
        var rankA = database.AddTreeRank(versionA, 1);
        var rankB = database.AddTreeRank(versionB, 1);
        var target = database.AddVehicle(tree, rankA, "Target", 100, versionA);
        var otherVersionVehicle = database.AddVehicle(tree, rankB, "Other version", 10, versionB);
        await database.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ResearchCalculationInputException>(() =>
            database.Calculator.CalculateResearchAsync(
                new ResearchCalculationRequest(
                    versionA.Id,
                    target.Id,
                    [],
                    [otherVersionVehicle.Id],
                    100,
                    int.MaxValue)));

        Assert.Contains("Filler target IDs", exception.Message);
        Assert.Contains(otherVersionVehicle.Id.ToString(), exception.Message);
        Assert.Contains(versionA.Id.ToString(), exception.Message);
    }

    [Fact]
    public async Task CalculateResearchAsync_ZeroAverageRpPerMatch_ThrowsArgumentOutOfRangeException()
    {
        using var database = new CalculatorTestDatabase();

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            database.Calculator.CalculateResearchAsync(
                new ResearchCalculationRequest(Guid.NewGuid(), Guid.NewGuid(), [], [], 0, 100)));

        Assert.Equal("AverageRpPerMatch", exception.ParamName);
    }

    [Fact]
    public async Task CalculateResearchAsync_NegativeAverageRpPerMatch_ThrowsArgumentOutOfRangeException()
    {
        using var database = new CalculatorTestDatabase();

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            database.Calculator.CalculateResearchAsync(
                new ResearchCalculationRequest(Guid.NewGuid(), Guid.NewGuid(), [], [], -1, 100)));

        Assert.Equal("AverageRpPerMatch", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CalculateResearchAsync_NonPositiveAverageNetSlPerMatch_ThrowsArgumentOutOfRangeException(
        int averageNetSlPerMatch)
    {
        using var database = new CalculatorTestDatabase();

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            database.Calculator.CalculateResearchAsync(
                new ResearchCalculationRequest(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    [],
                    [],
                    100,
                    averageNetSlPerMatch)));

        Assert.Equal("AverageNetSlPerMatch", exception.ParamName);
    }

    [Fact]
    public async Task CalculateResearchAsync_UnknownTargetVehicle_ThrowsInputException()
    {
        using var database = new CalculatorTestDatabase();
        var rank = database.AddTreeRank(database.AddNation(), 1);
        await database.SaveChangesAsync();
        var unknownTargetId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<ResearchCalculationInputException>(() =>
            database.Calculator.CalculateResearchAsync(
                new ResearchCalculationRequest(
                    rank.ResearchTreeVersionId,
                    unknownTargetId,
                    [],
                    [],
                    100,
                    int.MaxValue)));

        Assert.Contains(unknownTargetId.ToString(), exception.Message);
        Assert.Contains(rank.ResearchTreeVersionId.ToString(), exception.Message);
    }

    private static ResearchCalculationRequest RequestFor(
        Vehicle target,
        List<Guid>? unlockedVehicleIds = null,
        List<Guid>? fillerTargetIds = null,
        int averageRpPerMatch = 100,
        int averageNetSlPerMatch = int.MaxValue,
        ResearchTreeVersion? version = null,
        ResearchOptimizationMode optimizationMode = ResearchOptimizationMode.LeastRp)
    {
        var researchTreeVersionId = version?.Id
            ?? Assert.Single(target.TreeEntries).ResearchTreeVersionId;

        return new ResearchCalculationRequest(
            researchTreeVersionId,
            target.Id,
            unlockedVehicleIds ?? [],
            fillerTargetIds ?? [],
            averageRpPerMatch,
            averageNetSlPerMatch,
            optimizationMode);
    }

    private static void AssertVehicleIds(
        ResearchCalculationResult result,
        params Vehicle[] expectedVehicles)
    {
        Assert.Equal(expectedVehicles.Length, result.RequiredVehicles.Count);
        Assert.Equal(
            expectedVehicles.Select(vehicle => vehicle.Id).OrderBy(id => id),
            result.RequiredVehicles.Select(vehicle => vehicle.VehicleId).OrderBy(id => id));
    }

    // -----------------------------------------------------------------------
    // Batch 10 — automatic rank-gate candidate selection
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CalculateResearchAsync_AutoSelection_ResolvesDeficitWithCheapestCandidate()
    {
        // Rank 1 threshold = 2. The mandatory line provides 1 vehicle (mandatory).
        // Two candidates exist: expensive (500 RP) and cheap (100 RP).
        // Auto-selection should pick cheap (lowest RP) to satisfy the shortfall.
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rankOne = database.AddTreeRank(nation, 1, requiredVehiclesUnlocked: 2);
        var rankTwo = database.AddTreeRank(nation, 2);
        var mandatory = database.AddVehicle(rankOne, "Mandatory", 300, 0);
        var cheap = database.AddVehicle(rankOne, "Cheap", 100, 0);
        var expensive = database.AddVehicle(rankOne, "Expensive", 500, 0);
        var target = database.AddVehicle(rankTwo, "Target", 1_000, 0);
        database.AddPrerequisite(target, mandatory);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(RequestFor(target));

        // Deficit resolved — no rank deficits.
        Assert.Empty(result.RankDeficits);
        // cheap was auto-selected; expensive was not needed.
        Assert.Contains(result.RequiredVehicles, v => v.VehicleId == cheap.Id && v.IsRankGateFiller);
        Assert.DoesNotContain(result.RequiredVehicles, v => v.VehicleId == expensive.Id);
        Assert.Equal(1_400, result.TotalRpRequired); // mandatory + cheap + target
    }

    [Fact]
    public async Task CalculateResearchAsync_AutoSelection_IncludesTransitivePrerequisitesOfCandidate()
    {
        // Rank 1 threshold = 2. Mandatory line contributes 1 vehicle.
        // filler (50 RP) is cheaper than prereqOfFiller (150 RP), so the sorter
        // picks filler first. filler requires prereqOfFiller as a prerequisite,
        // so CollectAutoFillerLine must recursively gather prereqOfFiller too.
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rankOne = database.AddTreeRank(nation, 1, requiredVehiclesUnlocked: 2);
        var rankTwo = database.AddTreeRank(nation, 2);
        var mandatory = database.AddVehicle(rankOne, "Mandatory", 300, 0);
        var prereqOfFiller = database.AddVehicle(rankOne, "Prereq of filler", 150, 0);
        var filler = database.AddVehicle(rankOne, "Filler", 50, 0);
        var target = database.AddVehicle(rankTwo, "Target", 1_000, 0);
        database.AddPrerequisite(target, mandatory);
        database.AddPrerequisite(filler, prereqOfFiller);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(RequestFor(target));

        Assert.Empty(result.RankDeficits);

        // Both filler (directly auto-selected) and prereqOfFiller (pulled in
        // through filler's prerequisite chain) are included as rank-gate fillers.
        var fillerEntry = Assert.Single(result.RequiredVehicles, v => v.VehicleId == filler.Id);
        Assert.True(fillerEntry.IsRankGateFiller);
        var prereqEntry = Assert.Single(result.RequiredVehicles, v => v.VehicleId == prereqOfFiller.Id);
        Assert.True(prereqEntry.IsRankGateFiller);

        // Total RP = mandatory(300) + filler(50) + prereqOfFiller(150) + target(1000).
        Assert.Equal(1_500, result.TotalRpRequired);
    }

    [Fact]
    public async Task CalculateResearchAsync_AutoSelection_NeverSelectsNonResearchableCandidates()
    {
        // Rank 1 has a researchable mandatory and non-researchable (premium, squadron,
        // event, pack, hidden) vehicles. Auto-selection must ignore all non-researchable
        // entries regardless of their RP cost.
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rankOne = database.AddTreeRank(nation, 1, requiredVehiclesUnlocked: 3);
        var rankTwo = database.AddTreeRank(nation, 2);
        var mandatory = database.AddVehicle(rankOne, "Mandatory", 300, 0);
        database.AddVehicle(rankOne, "Premium", 0, 0, VehicleAcquisitionType.Premium);
        database.AddVehicle(rankOne, "Squadron", 0, 0, VehicleAcquisitionType.Squadron);
        database.AddVehicle(rankOne, "Event", 0, 0, VehicleAcquisitionType.Event);
        database.AddVehicle(rankOne, "Pack", 0, 0, VehicleAcquisitionType.Pack);
        database.AddVehicle(rankOne, "Hidden", 0, 0, VehicleAcquisitionType.Hidden);
        var target = database.AddVehicle(rankTwo, "Target", 1_000, 0);
        database.AddPrerequisite(target, mandatory);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(RequestFor(target));

        // Non-researchable vehicles cannot satisfy the shortfall — deficit persists.
        var deficit = Assert.Single(result.RankDeficits);
        Assert.Equal(1, deficit.RankNumber);
        Assert.Equal(2, deficit.Shortfall); // 3 required - 1 mandatory = 2 remaining

        // None of the non-researchable vehicles were auto-selected.
        Assert.All(
            result.RequiredVehicles,
            v => Assert.False(v.IsRankGateFiller));
    }

    [Fact]
    public async Task CalculateResearchAsync_AutoSelection_ReportsRemainingDeficitWhenCandidatesExhausted()
    {
        // Rank 1 threshold = 3. Mandatory contributes 1, only 1 extra researchable
        // candidate exists — auto-selection fills 1 but 1 shortfall remains.
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rankOne = database.AddTreeRank(nation, 1, requiredVehiclesUnlocked: 3);
        var rankTwo = database.AddTreeRank(nation, 2);
        var mandatory = database.AddVehicle(rankOne, "Mandatory", 200, 0);
        var onlyCandidate = database.AddVehicle(rankOne, "Only Candidate", 100, 0);
        var target = database.AddVehicle(rankTwo, "Target", 500, 0);
        database.AddPrerequisite(target, mandatory);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(RequestFor(target));

        // onlyCandidate was auto-selected.
        Assert.Contains(result.RequiredVehicles, v => v.VehicleId == onlyCandidate.Id && v.IsRankGateFiller);

        // Shortfall of 1 remains after exhausting all candidates.
        var deficit = Assert.Single(result.RankDeficits);
        Assert.Equal(1, deficit.RankNumber);
        Assert.Equal(1, deficit.Shortfall);
        Assert.Equal(3, deficit.Required);
    }

    [Fact]
    public async Task CalculateResearchAsync_AutoSelection_ExplicitFillersTakePrecedenceOverAutoSelection()
    {
        // Rank 1 threshold = 2. Mandatory gives 1. Player explicitly provides
        // explicitFiller, which satisfies the shortfall. auto-selection should
        // not add anything else — cheaperCandidate must NOT appear in the result.
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rankOne = database.AddTreeRank(nation, 1, requiredVehiclesUnlocked: 2);
        var rankTwo = database.AddTreeRank(nation, 2);
        var mandatory = database.AddVehicle(rankOne, "Mandatory", 300, 0);
        var cheaperCandidate = database.AddVehicle(rankOne, "Cheaper Candidate", 50, 0);
        var explicitFiller = database.AddVehicle(rankOne, "Explicit Filler", 400, 0);
        var target = database.AddVehicle(rankTwo, "Target", 1_000, 0);
        database.AddPrerequisite(target, mandatory);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(target, fillerTargetIds: [explicitFiller.Id]));

        Assert.Empty(result.RankDeficits);
        Assert.Contains(result.RequiredVehicles, v => v.VehicleId == explicitFiller.Id && v.IsRankGateFiller);
        // cheaperCandidate should not have been auto-selected since quota is already met.
        Assert.DoesNotContain(result.RequiredVehicles, v => v.VehicleId == cheaperCandidate.Id);
    }

    [Fact]
    public async Task CalculateResearchAsync_AutoSelection_DeterministicTieBreakingBySlThenName()
    {
        // Three candidates with identical RP cost. Tie-broken by SL cost asc,
        // then by name asc. Rank threshold requires exactly 2 total, mandatory
        // provides 1, so only one auto-selected candidate should be picked.
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rankOne = database.AddTreeRank(nation, 1, requiredVehiclesUnlocked: 2);
        var rankTwo = database.AddTreeRank(nation, 2);
        var mandatory = database.AddVehicle(rankOne, "Mandatory", 200, 1_000);
        // All three have RP=100; tie-break by SL then name picks cheapSlFirst.
        var highSlLast = database.AddVehicle(rankOne, "A High SL", 100, 5_000);
        var cheapSlFirst = database.AddVehicle(rankOne, "B Cheap SL", 100, 500);
        var midSl = database.AddVehicle(rankOne, "C Mid SL", 100, 2_000);
        var target = database.AddVehicle(rankTwo, "Target", 1_000, 0);
        database.AddPrerequisite(target, mandatory);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(RequestFor(target));

        Assert.Empty(result.RankDeficits);
        // cheapSlFirst (lowest SL) must be the auto-selected vehicle.
        Assert.Contains(result.RequiredVehicles, v => v.VehicleId == cheapSlFirst.Id && v.IsRankGateFiller);
        Assert.DoesNotContain(result.RequiredVehicles, v => v.VehicleId == highSlLast.Id);
        Assert.DoesNotContain(result.RequiredVehicles, v => v.VehicleId == midSl.Id);
    }

    // -----------------------------------------------------------------------
    // Batch 11 — route optimization modes
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CalculateResearchAsync_OptimizationMode_LeastRp_PrefersLowestRpOverLowestSl()
    {
        // Rank 1 requires 2 vehicles, mandatory provides 1 (shortfall = 1).
        // Candidate LowRp: 100 RP, 10 000 SL.
        // Candidate LowSl: 1 000 RP, 500 SL.
        // LeastRp should select LowRp.
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rankOne = database.AddTreeRank(nation, 1, requiredVehiclesUnlocked: 2);
        var rankTwo = database.AddTreeRank(nation, 2);
        var mandatory = database.AddVehicle(rankOne, "Mandatory", 300, 0);
        var lowRp = database.AddVehicle(rankOne, "Low RP", 100, 10_000);
        var lowSl = database.AddVehicle(rankOne, "Low SL", 1_000, 500);
        var target = database.AddVehicle(rankTwo, "Target", 5_000, 0);
        database.AddPrerequisite(target, mandatory);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(target, optimizationMode: ResearchOptimizationMode.LeastRp));

        Assert.Empty(result.RankDeficits);
        Assert.Contains(result.RequiredVehicles, v => v.VehicleId == lowRp.Id && v.IsRankGateFiller);
        Assert.DoesNotContain(result.RequiredVehicles, v => v.VehicleId == lowSl.Id);
        Assert.Equal(300 + 100 + 5_000, result.TotalRpRequired);
        Assert.Equal(10_000, result.TotalSlRequired);
    }

    [Fact]
    public async Task CalculateResearchAsync_OptimizationMode_LeastSl_PrefersLowestSlOverLowestRp()
    {
        // Same conflict as above, but with LeastSl. Should select LowSl.
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rankOne = database.AddTreeRank(nation, 1, requiredVehiclesUnlocked: 2);
        var rankTwo = database.AddTreeRank(nation, 2);
        var mandatory = database.AddVehicle(rankOne, "Mandatory", 300, 0);
        var lowRp = database.AddVehicle(rankOne, "Low RP", 100, 10_000);
        var lowSl = database.AddVehicle(rankOne, "Low SL", 1_000, 500);
        var target = database.AddVehicle(rankTwo, "Target", 5_000, 0);
        database.AddPrerequisite(target, mandatory);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(target, optimizationMode: ResearchOptimizationMode.LeastSl));

        Assert.Empty(result.RankDeficits);
        Assert.Contains(result.RequiredVehicles, v => v.VehicleId == lowSl.Id && v.IsRankGateFiller);
        Assert.DoesNotContain(result.RequiredVehicles, v => v.VehicleId == lowRp.Id);
        Assert.Equal(300 + 1_000 + 5_000, result.TotalRpRequired);
        Assert.Equal(500, result.TotalSlRequired);
    }

    [Fact]
    public async Task CalculateResearchAsync_OptimizationMode_FewestVehicles_PrefersSingleHighRpOverChainOfLowRp()
    {
        // Rank 1 requires 1 vehicle, satisfied by mandatoryOne.
        // Rank 2 requires 2 vehicles, mandatoryTwo provides 1 (shortfall = 1).
        // Candidate in Rank 2: SingleHighRp (500 RP, no prerequisites -> adds 1 vehicle).
        // Candidate in Rank 2: ChainChild (100 RP, requires ChainParent in Rank 1 -> adds 2 vehicles: ChainChild + ChainParent).
        // FewestVehicles should select SingleHighRp because it adds 1 vehicle instead of 2.
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rankOne = database.AddTreeRank(nation, 1, requiredVehiclesUnlocked: 1);
        var rankTwo = database.AddTreeRank(nation, 2, requiredVehiclesUnlocked: 2);
        var rankThree = database.AddTreeRank(nation, 3);
        var mandatoryOne = database.AddVehicle(rankOne, "Mandatory I", 300, 0);
        var chainParent = database.AddVehicle(rankOne, "Chain Parent", 100, 0);
        var mandatoryTwo = database.AddVehicle(rankTwo, "Mandatory II", 300, 0);
        var singleHighRp = database.AddVehicle(rankTwo, "Single High RP", 500, 0);
        var chainChild = database.AddVehicle(rankTwo, "Chain Child", 100, 0);
        var target = database.AddVehicle(rankThree, "Target", 1_000, 0);
        database.AddPrerequisite(mandatoryTwo, mandatoryOne);
        database.AddPrerequisite(target, mandatoryTwo);
        database.AddPrerequisite(chainChild, chainParent);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(target, optimizationMode: ResearchOptimizationMode.FewestVehicles));

        Assert.Empty(result.RankDeficits);
        // Only 1 filler added: singleHighRp.
        var fillers = result.RequiredVehicles.Where(v => v.IsRankGateFiller).ToList();
        var filler = Assert.Single(fillers);
        Assert.Equal(singleHighRp.Id, filler.VehicleId);
        Assert.DoesNotContain(result.RequiredVehicles, v => v.VehicleId == chainChild.Id);
        Assert.DoesNotContain(result.RequiredVehicles, v => v.VehicleId == chainParent.Id);
    }

    [Fact]
    public async Task CalculateResearchAsync_OptimizationMode_LeastRp_PrefersChainOfLowRpOverSingleHighRp()
    {
        // Same setup as above, but with LeastRp.
        // LeastRp should select chainChild (100 RP, pulling chainParent 100 RP, total 200 RP < 500 RP).
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rankOne = database.AddTreeRank(nation, 1, requiredVehiclesUnlocked: 1);
        var rankTwo = database.AddTreeRank(nation, 2, requiredVehiclesUnlocked: 2);
        var rankThree = database.AddTreeRank(nation, 3);
        var mandatoryOne = database.AddVehicle(rankOne, "Mandatory I", 300, 0);
        var chainParent = database.AddVehicle(rankOne, "Chain Parent", 100, 0);
        var mandatoryTwo = database.AddVehicle(rankTwo, "Mandatory II", 300, 0);
        var singleHighRp = database.AddVehicle(rankTwo, "Single High RP", 500, 0);
        var chainChild = database.AddVehicle(rankTwo, "Chain Child", 100, 0);
        var target = database.AddVehicle(rankThree, "Target", 1_000, 0);
        database.AddPrerequisite(mandatoryTwo, mandatoryOne);
        database.AddPrerequisite(target, mandatoryTwo);
        database.AddPrerequisite(chainChild, chainParent);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(target, optimizationMode: ResearchOptimizationMode.LeastRp));

        Assert.Empty(result.RankDeficits);
        var fillers = result.RequiredVehicles.Where(v => v.IsRankGateFiller).ToList();
        Assert.Equal(2, fillers.Count);
        Assert.Contains(fillers, v => v.VehicleId == chainChild.Id);
        Assert.Contains(fillers, v => v.VehicleId == chainParent.Id);
        Assert.DoesNotContain(result.RequiredVehicles, v => v.VehicleId == singleHighRp.Id);
        Assert.Equal(300 + 300 + 100 + 100 + 1_000, result.TotalRpRequired);
    }

    [Fact]
    public async Task CalculateResearchAsync_OptimizationMode_FewestMatches_SlBottlenecked_PrefersHighRpZeroSlOverLowRpHighSl()
    {
        // When SL is the bottleneck, grinding high-RP / zero-SL vehicles does not increase matches,
        // while grinding high-SL vehicles increases match requirements.
        // Target: 1 000 RP, 20 000 SL. avgRp=1 000, avgSl=1 000.
        // Target alone: 1 RP match, 20 SL matches (total 20 matches).
        // Candidate ZeroSl: 5 000 RP, 0 SL -> Projected: 6 000 RP (6 matches), 20 000 SL (20 matches) -> 20 matches.
        // Candidate HighSl: 100 RP, 5 000 SL -> Projected: 1 100 RP (2 matches), 25 000 SL (25 matches) -> 25 matches.
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rankOne = database.AddTreeRank(nation, 1, requiredVehiclesUnlocked: 2);
        var rankTwo = database.AddTreeRank(nation, 2);
        var mandatory = database.AddVehicle(rankOne, "Mandatory", 0, 0);
        var zeroSl = database.AddVehicle(rankOne, "Zero SL High RP", 5_000, 0);
        var highSl = database.AddVehicle(rankOne, "High SL Low RP", 100, 5_000);
        var target = database.AddVehicle(rankTwo, "Target", 1_000, 20_000);
        database.AddPrerequisite(target, mandatory);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(
                target,
                averageRpPerMatch: 1_000,
                averageNetSlPerMatch: 1_000,
                optimizationMode: ResearchOptimizationMode.FewestMatches));

        Assert.Empty(result.RankDeficits);
        Assert.Equal(20, result.EstimatedMatches);
        Assert.Contains(result.RequiredVehicles, v => v.VehicleId == zeroSl.Id && v.IsRankGateFiller);
        Assert.DoesNotContain(result.RequiredVehicles, v => v.VehicleId == highSl.Id);
    }

    [Fact]
    public async Task CalculateResearchAsync_OptimizationMode_FewestMatches_RpBottlenecked_PrefersZeroRpHighSlOverHighRpLowSl()
    {
        // When RP is the bottleneck, high SL / zero RP does not increase matches,
        // while high RP increases match requirements.
        // Target: 20 000 RP, 1 000 SL. avgRp=1 000, avgSl=1 000.
        // Target alone: 20 RP matches, 1 SL match (total 20 matches).
        // Candidate ZeroRp: 0 RP, 5 000 SL -> Projected: 20 000 RP (20 matches), 6 000 SL (6 matches) -> 20 matches.
        // Candidate HighRp: 5 000 RP, 100 SL -> Projected: 25 000 RP (25 matches), 1 100 SL (2 matches) -> 25 matches.
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rankOne = database.AddTreeRank(nation, 1, requiredVehiclesUnlocked: 2);
        var rankTwo = database.AddTreeRank(nation, 2);
        var mandatory = database.AddVehicle(rankOne, "Mandatory", 0, 0);
        var zeroRp = database.AddVehicle(rankOne, "Zero RP High SL", 0, 5_000);
        var highRp = database.AddVehicle(rankOne, "High RP Low SL", 5_000, 100);
        var target = database.AddVehicle(rankTwo, "Target", 20_000, 1_000);
        database.AddPrerequisite(target, mandatory);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(
                target,
                averageRpPerMatch: 1_000,
                averageNetSlPerMatch: 1_000,
                optimizationMode: ResearchOptimizationMode.FewestMatches));

        Assert.Empty(result.RankDeficits);
        Assert.Equal(20, result.EstimatedMatches);
        Assert.Contains(result.RequiredVehicles, v => v.VehicleId == zeroRp.Id && v.IsRankGateFiller);
        Assert.DoesNotContain(result.RequiredVehicles, v => v.VehicleId == highRp.Id);
    }

    [Fact]
    public async Task CalculateResearchAsync_OptimizationMode_DefaultIsLeastRp_PreservesBackwardCompatibility()
    {
        // When optimizationMode is omitted, it defaults to LeastRp.
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rankOne = database.AddTreeRank(nation, 1, requiredVehiclesUnlocked: 2);
        var rankTwo = database.AddTreeRank(nation, 2);
        var mandatory = database.AddVehicle(rankOne, "Mandatory", 300, 0);
        var lowRp = database.AddVehicle(rankOne, "Low RP", 100, 10_000);
        var lowSl = database.AddVehicle(rankOne, "Low SL", 1_000, 500);
        var target = database.AddVehicle(rankTwo, "Target", 5_000, 0);
        database.AddPrerequisite(target, mandatory);
        await database.SaveChangesAsync();

        // Calling constructor with 6 arguments (omitting optimizationMode).
        var request = new ResearchCalculationRequest(
            Assert.Single(target.TreeEntries).ResearchTreeVersionId,
            target.Id,
            [],
            [],
            100,
            int.MaxValue);

        var result = await database.Calculator.CalculateResearchAsync(request);

        Assert.Empty(result.RankDeficits);
        Assert.Contains(result.RequiredVehicles, v => v.VehicleId == lowRp.Id && v.IsRankGateFiller);
        Assert.DoesNotContain(result.RequiredVehicles, v => v.VehicleId == lowSl.Id);
    }

    [Theory]
    [InlineData(ResearchOptimizationMode.LeastRp)]
    [InlineData(ResearchOptimizationMode.LeastSl)]
    [InlineData(ResearchOptimizationMode.FewestVehicles)]
    [InlineData(ResearchOptimizationMode.FewestMatches)]
    public async Task CalculateResearchAsync_OptimizationMode_AllModes_ExcludeNonResearchableVehicles(
        ResearchOptimizationMode mode)
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rankOne = database.AddTreeRank(nation, 1, requiredVehiclesUnlocked: 2);
        var rankTwo = database.AddTreeRank(nation, 2);
        var mandatory = database.AddVehicle(rankOne, "Mandatory", 300, 0);
        // Non-researchable entries with 0 costs:
        database.AddVehicle(rankOne, "Free Premium", 0, 0, VehicleAcquisitionType.Premium);
        database.AddVehicle(rankOne, "Free Squadron", 0, 0, VehicleAcquisitionType.Squadron);
        database.AddVehicle(rankOne, "Free Event", 0, 0, VehicleAcquisitionType.Event);
        database.AddVehicle(rankOne, "Free Pack", 0, 0, VehicleAcquisitionType.Pack);
        database.AddVehicle(rankOne, "Free Hidden", 0, 0, VehicleAcquisitionType.Hidden);
        // Only valid candidate:
        var validCandidate = database.AddVehicle(rankOne, "Valid Candidate", 500, 500);
        var target = database.AddVehicle(rankTwo, "Target", 1_000, 0);
        database.AddPrerequisite(target, mandatory);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(target, optimizationMode: mode));

        Assert.Empty(result.RankDeficits);
        var filler = Assert.Single(result.RequiredVehicles, v => v.IsRankGateFiller);
        Assert.Equal(validCandidate.Id, filler.VehicleId);
    }

    [Theory]
    [InlineData(ResearchOptimizationMode.LeastRp)]
    [InlineData(ResearchOptimizationMode.LeastSl)]
    [InlineData(ResearchOptimizationMode.FewestVehicles)]
    [InlineData(ResearchOptimizationMode.FewestMatches)]
    public async Task CalculateResearchAsync_OptimizationMode_AllModes_DeterministicTieBreakingEndsOnName(
        ResearchOptimizationMode mode)
    {
        // When all metrics are equal (same RP, same SL, 0 prereqs, same matches),
        // tie-breaking must consistently choose Candidate Alpha over Candidate Beta.
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rankOne = database.AddTreeRank(nation, 1, requiredVehiclesUnlocked: 2);
        var rankTwo = database.AddTreeRank(nation, 2);
        var mandatory = database.AddVehicle(rankOne, "Mandatory", 300, 0);
        var beta = database.AddVehicle(rankOne, "Candidate Beta", 100, 100);
        var alpha = database.AddVehicle(rankOne, "Candidate Alpha", 100, 100);
        var target = database.AddVehicle(rankTwo, "Target", 1_000, 0);
        database.AddPrerequisite(target, mandatory);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(target, optimizationMode: mode));

        Assert.Empty(result.RankDeficits);
        var filler = Assert.Single(result.RequiredVehicles, v => v.IsRankGateFiller);
        Assert.Equal(alpha.Id, filler.VehicleId);
        Assert.Equal("Candidate Alpha", filler.Name);
    }

    [Fact]
    public async Task CalculateResearchAsync_InvalidOptimizationMode_ThrowsArgumentOutOfRangeException()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rank = database.AddTreeRank(nation, 1);
        var target = database.AddVehicle(rank, "Target", 100);
        await database.SaveChangesAsync();

        var request = RequestFor(target, optimizationMode: (ResearchOptimizationMode)99);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            database.Calculator.CalculateResearchAsync(request));

        Assert.Equal("OptimizationMode", exception.ParamName);
    }
}

public class ResearchControllerTests
{
    [Theory]
    [InlineData(0, 100, "Average RP per match")]
    [InlineData(-1, 100, "Average RP per match")]
    [InlineData(100, 0, "Average net SL per match")]
    [InlineData(100, -1, "Average net SL per match")]
    public async Task CalculateResearch_NonPositiveAverage_ReturnsBadRequest(
        int averageRpPerMatch,
        int averageNetSlPerMatch,
        string expectedMessage)
    {
        using var database = new CalculatorTestDatabase();
        var controller = new GrindingThunder.Api.Controllers.ResearchController(database.Calculator);

        var result = await controller.CalculateResearch(
            new ResearchCalculationRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                [],
                [],
                averageRpPerMatch,
                averageNetSlPerMatch),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains(expectedMessage, Assert.IsType<string>(badRequest.Value));
    }

    [Fact]
    public async Task CalculateResearch_UnknownVersion_ReturnsNotFound()
    {
        using var database = new CalculatorTestDatabase();
        var versionId = Guid.NewGuid();
        var controller = new GrindingThunder.Api.Controllers.ResearchController(database.Calculator);

        var result = await controller.CalculateResearch(
            new ResearchCalculationRequest(versionId, Guid.NewGuid(), [], [], 100, int.MaxValue),
            CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Contains(versionId.ToString(), Assert.IsType<string>(notFound.Value));
    }

    [Fact]
    public async Task CalculateResearch_TargetOutsideSelectedVersion_ReturnsBadRequest()
    {
        using var database = new CalculatorTestDatabase();
        var rank = database.AddTreeRank(database.AddNation(), 1);
        await database.SaveChangesAsync();
        var controller = new GrindingThunder.Api.Controllers.ResearchController(database.Calculator);

        var result = await controller.CalculateResearch(
            new ResearchCalculationRequest(
                rank.ResearchTreeVersionId,
                Guid.NewGuid(),
                [],
                [],
                100,
                int.MaxValue),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Target vehicle", Assert.IsType<string>(badRequest.Value));
    }

    [Fact]
    public async Task CalculateResearch_TargetNonResearchable_ReturnsBadRequest()
    {
        using var database = new CalculatorTestDatabase();
        var rank = database.AddTreeRank(database.AddNation(), 1);
        var premium = database.AddVehicle(rank, "Premium", 0, acquisitionType: VehicleAcquisitionType.Premium);
        await database.SaveChangesAsync();
        var controller = new GrindingThunder.Api.Controllers.ResearchController(database.Calculator);

        var result = await controller.CalculateResearch(
            new ResearchCalculationRequest(
                rank.ResearchTreeVersionId,
                premium.Id,
                [],
                [],
                100,
                int.MaxValue),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("not researchable", Assert.IsType<string>(badRequest.Value));
    }

    [Fact]
    public async Task CalculateResearch_FillerNonResearchable_ReturnsBadRequest()
    {
        using var database = new CalculatorTestDatabase();
        var rank = database.AddTreeRank(database.AddNation(), 1);
        var target = database.AddVehicle(rank, "Target", 100);
        var squadron = database.AddVehicle(rank, "Squadron", 100, acquisitionType: VehicleAcquisitionType.Squadron);
        await database.SaveChangesAsync();
        var controller = new GrindingThunder.Api.Controllers.ResearchController(database.Calculator);

        var result = await controller.CalculateResearch(
            new ResearchCalculationRequest(
                rank.ResearchTreeVersionId,
                target.Id,
                [],
                [squadron.Id],
                100,
                int.MaxValue),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Filler target IDs must be researchable", Assert.IsType<string>(badRequest.Value));
    }

    [Fact]
    public async Task CalculateResearch_InvalidOptimizationMode_ReturnsBadRequest()
    {
        using var database = new CalculatorTestDatabase();
        var rank = database.AddTreeRank(database.AddNation(), 1);
        var target = database.AddVehicle(rank, "Target", 100);
        await database.SaveChangesAsync();
        var controller = new GrindingThunder.Api.Controllers.ResearchController(database.Calculator);

        var result = await controller.CalculateResearch(
            new ResearchCalculationRequest(
                rank.ResearchTreeVersionId,
                target.Id,
                [],
                [],
                100,
                int.MaxValue,
                (ResearchOptimizationMode)99),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Invalid optimization mode", Assert.IsType<string>(badRequest.Value));
    }
}
