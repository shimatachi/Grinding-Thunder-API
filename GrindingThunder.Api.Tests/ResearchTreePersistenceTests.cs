using GrindingThunder.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class ResearchTreePersistenceTests
{
    [Fact]
    public async Task DbInitializer_CreatesStandardVehicleTypesAndGroundTrees()
    {
        using var database = new CalculatorTestDatabase();

        await DbInitializer.InitializeAsync(database.Context);

        var vehicleTypeNames = await database.Context.VehicleTypes
            .AsNoTracking()
            .Select(vt => vt.Name)
            .OrderBy(name => name)
            .ToListAsync();
        Assert.Equal(
            new[] { "Aviation", "Bluewater Fleet", "Coastal Fleet", "Ground", "Helicopter" },
            vehicleTypeNames);

        var trees = await database.Context.ResearchTrees
            .AsNoTracking()
            .Include(rt => rt.VehicleType)
            .Include(rt => rt.Versions).ThenInclude(v => v.TreeRanks)
            .ToListAsync();
        Assert.Equal(4, trees.Count);
        Assert.All(trees, tree => Assert.Equal("Ground", tree.VehicleType.Name));
        // Rank configuration is version-specific (Batch 4): every seeded tree
        // receives one version with its own eight TreeRank rows.
        Assert.All(trees, tree =>
        {
            var version = Assert.Single(tree.Versions);
            Assert.Equal(8, version.TreeRanks.Count);
        });
    }

    [Fact]
    public async Task VehicleType_CanBePersisted()
    {
        using var database = new CalculatorTestDatabase();
        var vehicleType = database.AddVehicleType("Aviation");

        await database.SaveChangesAsync();

        var persisted = await database.Context.VehicleTypes
            .AsNoTracking()
            .SingleAsync(vt => vt.Id == vehicleType.Id);
        Assert.Equal("Aviation", persisted.Name);
    }

    [Fact]
    public async Task ResearchTree_LinksNationAndVehicleType()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var vehicleType = database.AddVehicleType("Ground");
        var researchTree = database.AddResearchTree(nation, vehicleType);

        await database.SaveChangesAsync();
        database.Context.ChangeTracker.Clear();

        var persisted = await database.Context.ResearchTrees
            .AsNoTracking()
            .Include(rt => rt.Nation)
            .Include(rt => rt.VehicleType)
            .SingleAsync(rt => rt.Id == researchTree.Id);

        Assert.Equal("USA", persisted.Nation.Name);
        Assert.Equal("Ground", persisted.VehicleType.Name);
    }

    [Fact]
    public async Task Nation_CanOwnMultipleResearchTrees()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var ground = database.AddVehicleType("Ground");
        var aviation = database.AddVehicleType("Aviation");
        database.AddResearchTree(nation, ground);
        database.AddResearchTree(nation, aviation);

        await database.SaveChangesAsync();

        var trees = await database.Context.ResearchTrees
            .AsNoTracking()
            .Where(rt => rt.NationId == nation.Id)
            .ToListAsync();
        Assert.Equal(2, trees.Count);
    }

    [Fact]
    public async Task DuplicateNationAndVehicleType_IsRejectedByDatabase()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var ground = database.AddVehicleType("Ground");
        database.AddResearchTree(nation, ground);
        database.AddResearchTree(nation, ground);

        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
    }
}
