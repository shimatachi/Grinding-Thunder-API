using Microsoft.AspNetCore.Mvc;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Controllers;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using GrindingThunder.Api.Infrastructure.Services;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class ResearchTreeVersionsControllerTests
{
    [Fact]
    public async Task GetDiff_ReturnsOkWithDiffResult_WhenVersionsExist()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);
        var controller = new ResearchTreeVersionsController(lifecycleService);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update1 = database.AddGameUpdate("2.41");
        var update2 = database.AddGameUpdate("2.43");

        var v1 = database.AddResearchTreeVersion(tree, update1);
        var v2 = database.AddResearchTreeVersion(tree, update2);
        database.AddTreeRank(v1, 1, 1);
        database.AddTreeRank(v2, 1, 2);
        await database.SaveChangesAsync();

        var result = await controller.GetDiff(v1.Id, v2.Id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var diff = Assert.IsType<VersionDiffResult>(okResult.Value);
        Assert.Equal(v1.Id, diff.SourceVersionId);
        Assert.Equal(v2.Id, diff.TargetVersionId);
    }

    [Fact]
    public async Task GetDiff_ReturnsBadRequest_WhenSourceOrTargetIsEmpty()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);
        var controller = new ResearchTreeVersionsController(lifecycleService);

        var result1 = await controller.GetDiff(Guid.Empty, Guid.NewGuid(), CancellationToken.None);
        var badRequest1 = Assert.IsType<BadRequestObjectResult>(result1);
        Assert.Contains("sourceVersionId is required", badRequest1.Value?.ToString());

        var result2 = await controller.GetDiff(Guid.NewGuid(), Guid.Empty, CancellationToken.None);
        var badRequest2 = Assert.IsType<BadRequestObjectResult>(result2);
        Assert.Contains("targetVersionId is required", badRequest2.Value?.ToString());
    }

    [Fact]
    public async Task GetDiff_ReturnsNotFound_WhenVersionDoesNotExist()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);
        var controller = new ResearchTreeVersionsController(lifecycleService);

        var result = await controller.GetDiff(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task ValidateVersion_ReturnsOkWithValidationResult_WhenVersionExists()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);
        var controller = new ResearchTreeVersionsController(lifecycleService);

        var tree = database.AddResearchTree(database.AddNation(), database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree, update);
        var rank = database.AddTreeRank(version, 1, 1);
        var veh = new Vehicle { Id = Guid.NewGuid(), ResearchTreeId = tree.Id, Name = "Tank" };
        database.Context.Vehicles.Add(veh);
        database.AddVehicleTreeEntry(version, veh, rank, 1000);
        await database.SaveChangesAsync();

        var result = await controller.ValidateVersion(version.Id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var validation = Assert.IsType<VersionValidationResult>(okResult.Value);
        Assert.True(validation.IsValid);
    }

    [Fact]
    public async Task ValidateVersion_ReturnsNotFound_WhenVersionDoesNotExist()
    {
        using var database = new CalculatorTestDatabase();
        var lifecycleService = new VersionLifecycleService(database.Context);
        var controller = new ResearchTreeVersionsController(lifecycleService);

        var result = await controller.ValidateVersion(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }
}

