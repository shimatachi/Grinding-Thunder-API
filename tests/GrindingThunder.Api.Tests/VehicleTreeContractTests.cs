using System.Text.Json;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Controllers;
using GrindingThunder.Api.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class VehicleTreeContractTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetVehicleTree_ReturnsStronglyTypedDto_WithExactCamelCaseContract()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var ground = database.AddVehicleType("Ground");
        var update = database.AddGameUpdate("2.43");
        var tree = database.AddResearchTree(nation, ground);
        var version = database.AddResearchTreeVersion(tree, update);
        var rank1 = database.AddTreeRank(version, 1, requiredVehiclesUnlocked: 0);

        var parentTank = new Vehicle
        {
            Id = Guid.NewGuid(),
            ResearchTreeId = tree.Id,
            Name = "M4 Sherman",
            ImageUrl = "https://example.test/m4.png",
            WikiUrl = "https://wiki.warthunder.com/M4_Sherman"
        };
        var childTank = new Vehicle
        {
            Id = Guid.NewGuid(),
            ResearchTreeId = tree.Id,
            Name = "M4A1 Sherman",
            ImageUrl = "https://example.test/m4a1.png"
        };
        var premiumTank = new Vehicle
        {
            Id = Guid.NewGuid(),
            ResearchTreeId = tree.Id,
            Name = "Calliope"
        };
        database.Context.Vehicles.AddRange(parentTank, childTank, premiumTank);

        var parentEntry = database.AddVehicleTreeEntry(
            version, parentTank, rank1, rpCost: 10_000, slCost: 20_000, treeColumn: 2, treeRow: 1);
        var childEntry = database.AddVehicleTreeEntry(
            version, childTank, rank1, rpCost: 12_000, slCost: 25_000, treeColumn: 2, treeRow: 1, folderParent: parentEntry);
        database.AddVehicleTreeEntry(
            version, premiumTank, rank1, rpCost: 0, slCost: 0, treeColumn: 5, treeRow: 1, acquisitionType: VehicleAcquisitionType.Premium);

        database.AddPrerequisite(childTank, parentTank, version);
        await database.SaveChangesAsync();

        version.Status = ResearchTreeVersionStatus.Published;
        await database.SaveChangesAsync();

        var controller = new VehiclesController(database.Context);
        var actionResult = await controller.GetVehicleTree(nation.Id, version.Id, "Ground", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var treeEntries = Assert.IsAssignableFrom<IReadOnlyList<VehicleTreeResponseDto>>(okResult.Value);

        Assert.Equal(3, treeEntries.Count);

        // Verify strongly typed model properties
        var parentDto = treeEntries.Single(e => e.Id == parentTank.Id);
        Assert.Equal(tree.Id, parentDto.ResearchTreeId);
        Assert.Equal(nation.Id, parentDto.NationId);
        Assert.Equal("USA", parentDto.NationName);
        Assert.Equal(ground.Id, parentDto.VehicleTypeId);
        Assert.Equal("Ground", parentDto.VehicleTypeName);
        Assert.Equal(rank1.Id, parentDto.TreeRankId);
        Assert.Equal(version.Id, parentDto.ResearchTreeVersionId);
        Assert.Equal("M4 Sherman", parentDto.Name);
        Assert.Equal("https://example.test/m4.png", parentDto.ImageUrl);
        Assert.Equal("https://wiki.warthunder.com/M4_Sherman", parentDto.WikiUrl);
        Assert.Equal(10_000, parentDto.RpCost);
        Assert.Equal(20_000, parentDto.SlCost);
        Assert.Equal(VehicleAcquisitionType.Researchable, parentDto.AcquisitionType);
        Assert.True(parentDto.IsFolderParent);
        Assert.Null(parentDto.FolderParentId);
        Assert.Equal(2, parentDto.TreeColumn);
        Assert.Equal(1, parentDto.TreeRow);
        Assert.Empty(parentDto.PrerequisiteIds);

        var childDto = treeEntries.Single(e => e.Id == childTank.Id);
        Assert.False(childDto.IsFolderParent);
        Assert.Equal(parentTank.Id, childDto.FolderParentId);
        Assert.Single(childDto.PrerequisiteIds);
        Assert.Equal(parentTank.Id, childDto.PrerequisiteIds[0]);

        var premiumDto = treeEntries.Single(e => e.Id == premiumTank.Id);
        Assert.Equal(VehicleAcquisitionType.Premium, premiumDto.AcquisitionType);

        // Verify JSON serialization contract matches expected camelCase wire properties
        var json = JsonSerializer.Serialize(treeEntries, WebJsonOptions);
        using var doc = JsonDocument.Parse(json);
        var array = doc.RootElement;
        Assert.Equal(JsonValueKind.Array, array.ValueKind);
        Assert.Equal(3, array.GetArrayLength());

        var firstElement = array[0];
        string[] expectedPropertyNames =
        [
            "id",
            "researchTreeId",
            "nationId",
            "nationName",
            "vehicleTypeId",
            "vehicleTypeName",
            "treeRankId",
            "researchTreeVersionId",
            "name",
            "imageUrl",
            "wikiUrl",
            "rpCost",
            "slCost",
            "acquisitionType",
            "isFolderParent",
            "folderParentId",
            "treeColumn",
            "treeRow",
            "prerequisiteIds"
        ];

        foreach (var propName in expectedPropertyNames)
        {
            Assert.True(
                firstElement.TryGetProperty(propName, out _),
                $"Serialized JSON payload was missing expected contract property '{propName}'. Payload: {json}");
        }
    }
}
