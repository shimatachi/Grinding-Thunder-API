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

        Assert.Equal(16_100, result.TotalRpRequired);
        Assert.Equal(17, result.EstimatedMatches);
        Assert.Equal(
            new[] { "M2 Light", "M3 Stuart", "M4A1 Sherman" }.OrderBy(name => name),
            result.RequiredVehicles.Select(vehicle => vehicle.Name).OrderBy(name => name));

        var deficit = Assert.Single(result.RankDeficits);
        Assert.Equal(1, deficit.RankNumber);
        Assert.Equal(3, deficit.Shortfall);
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
        var a = database.AddVehicle(rank, "A", 10);
        var b = database.AddVehicle(rank, "B", 20);
        var c = database.AddVehicle(rank, "C", 30);
        var d = database.AddVehicle(rank, "D", 40);
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
        var a = database.AddVehicle(rank, "A", 10);
        var b = database.AddVehicle(rank, "B", 20);
        var c = database.AddVehicle(rank, "C", 30);
        var d = database.AddVehicle(rank, "D", 40);
        database.AddPrerequisite(b, a);
        database.AddPrerequisite(c, b);
        database.AddPrerequisite(d, c);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(
            RequestFor(d, unlockedVehicleIds: [c.Id]));

        AssertVehicleIds(result, d);
        Assert.Equal(40, result.TotalRpRequired);
    }

    [Fact]
    public async Task CalculateResearchAsync_SharedPrerequisite_IncludesSharedVehicleOnce()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation();
        var rank = database.AddTreeRank(nation, 1);
        var a = database.AddVehicle(rank, "A", 10);
        var b = database.AddVehicle(rank, "B", 20);
        var c = database.AddVehicle(rank, "C", 30);
        var d = database.AddVehicle(rank, "D", 40);
        database.AddPrerequisite(b, a);
        database.AddPrerequisite(c, a);
        database.AddPrerequisite(d, b);
        database.AddPrerequisite(d, c);
        await database.SaveChangesAsync();

        var result = await database.Calculator.CalculateResearchAsync(RequestFor(d));

        AssertVehicleIds(result, d, b, c, a);
        Assert.Single(result.RequiredVehicles, vehicle => vehicle.VehicleId == a.Id);
        Assert.Equal(100, result.TotalRpRequired);
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
        var c = database.AddVehicle(rank, "C", 30);
        var d = database.AddVehicle(rank, "D", 40);
        var e = database.AddVehicle(rank, "E", 50);
        var f = database.AddVehicle(rank, "F", 60);
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
    public async Task CalculateResearchAsync_FirstUnsatisfiedRankGate_ReturnsOnlyFirstDeficit()
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

        var deficit = Assert.Single(result.RankDeficits);
        Assert.Equal(2, deficit.RankNumber);
        Assert.Equal(1, deficit.Shortfall);
        Assert.Equal(2, deficit.Required);
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
        Assert.Equal(4, result.EstimatedMatches);
        Assert.Equal(
            (int)Math.Ceiling((double)result.TotalRpRequired / 30),
            result.EstimatedMatches);
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
            new ResearchCalculationRequest(versionA.Id, target.Id, [], [], 100));
        var resultB = await database.Calculator.CalculateResearchAsync(
            new ResearchCalculationRequest(versionB.Id, target.Id, [], [], 100));

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
            new ResearchCalculationRequest(versionA.Id, target.Id, [], [], 100));
        var resultB = await database.Calculator.CalculateResearchAsync(
            new ResearchCalculationRequest(versionB.Id, target.Id, [], [], 100));

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
                new ResearchCalculationRequest(versionA.Id, target.Id, [], [], 100)));

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
                new ResearchCalculationRequest(unknownVersionId, Guid.NewGuid(), [], [], 100)));

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
                    100)));

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
                    100)));

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
                new ResearchCalculationRequest(Guid.NewGuid(), Guid.NewGuid(), [], [], 0)));

        Assert.Equal("AverageRpPerMatch", exception.ParamName);
    }

    [Fact]
    public async Task CalculateResearchAsync_NegativeAverageRpPerMatch_ThrowsArgumentOutOfRangeException()
    {
        using var database = new CalculatorTestDatabase();

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            database.Calculator.CalculateResearchAsync(
                new ResearchCalculationRequest(Guid.NewGuid(), Guid.NewGuid(), [], [], -1)));

        Assert.Equal("AverageRpPerMatch", exception.ParamName);
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
                    100)));

        Assert.Contains(unknownTargetId.ToString(), exception.Message);
        Assert.Contains(rank.ResearchTreeVersionId.ToString(), exception.Message);
    }

    private static ResearchCalculationRequest RequestFor(
        Vehicle target,
        List<Guid>? unlockedVehicleIds = null,
        List<Guid>? fillerTargetIds = null,
        int averageRpPerMatch = 100,
        ResearchTreeVersion? version = null)
    {
        var researchTreeVersionId = version?.Id
            ?? Assert.Single(target.TreeEntries).ResearchTreeVersionId;

        return new ResearchCalculationRequest(
            researchTreeVersionId,
            target.Id,
            unlockedVehicleIds ?? [],
            fillerTargetIds ?? [],
            averageRpPerMatch);
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
}

public class ResearchControllerTests
{
    [Fact]
    public async Task CalculateResearch_UnknownVersion_ReturnsNotFound()
    {
        using var database = new CalculatorTestDatabase();
        var versionId = Guid.NewGuid();
        var controller = new GrindingThunder.Api.Controllers.ResearchController(database.Calculator);

        var result = await controller.CalculateResearch(
            new ResearchCalculationRequest(versionId, Guid.NewGuid(), [], [], 100),
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
                100),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Target vehicle", Assert.IsType<string>(badRequest.Value));
    }
}
