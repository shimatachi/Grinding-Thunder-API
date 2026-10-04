using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class TreeRankTests
{
    [Fact]
    public async Task TreeRank_BelongsToASpecificVersion()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var tree = database.AddResearchTree(nation, database.AddVehicleType("Ground"));
        var updateA = database.AddGameUpdate("2.43");
        var updateB = database.AddGameUpdate("2.45");
        var versionA = database.AddResearchTreeVersion(tree, updateA);
        var versionB = database.AddResearchTreeVersion(tree, updateB);

        var rankAv1 = database.AddTreeRank(versionA, 1, requiredVehiclesUnlocked: 3);
        var rankBv1 = database.AddTreeRank(versionB, 1, requiredVehiclesUnlocked: 5);

        await database.SaveChangesAsync();
        database.Context.ChangeTracker.Clear();

        var persistedA = await database.Context.TreeRanks
            .AsNoTracking()
            .SingleAsync(tr => tr.Id == rankAv1.Id);
        var persistedB = await database.Context.TreeRanks
            .AsNoTracking()
            .SingleAsync(tr => tr.Id == rankBv1.Id);

        Assert.Equal(versionA.Id, persistedA.ResearchTreeVersionId);
        Assert.Equal(versionB.Id, persistedB.ResearchTreeVersionId);

        // Same rank number in different versions is allowed and can carry
        // different requirements (rank-gate rules are version-specific).
        Assert.Equal(3, persistedA.RequiredVehiclesUnlocked);
        Assert.Equal(5, persistedB.RequiredVehiclesUnlocked);
    }

    [Fact]
    public async Task TreeRank_DuplicateRankNumberInSameVersion_IsRejected()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var tree = database.AddResearchTree(nation, database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree, update);

        database.AddTreeRank(version, 1);
        database.AddTreeRank(version, 1);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => database.SaveChangesAsync());
    }

    [Fact]
    public async Task TreeRank_DeletingVersion_CascadesItsRanks()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var tree = database.AddResearchTree(nation, database.AddVehicleType("Ground"));
        var updateA = database.AddGameUpdate("2.43");
        var updateB = database.AddGameUpdate("2.45");
        var versionA = database.AddResearchTreeVersion(tree, updateA);
        var versionB = database.AddResearchTreeVersion(tree, updateB);

        var rankA = database.AddTreeRank(versionA, 1);
        database.AddTreeRank(versionB, 1);

        await database.SaveChangesAsync();

        database.Context.ResearchTreeVersions.Remove(versionA);
        await database.SaveChangesAsync();

        Assert.False(await database.Context.TreeRanks
            .AsNoTracking()
            .AnyAsync(tr => tr.Id == rankA.Id));
        Assert.Equal(1, await database.Context.TreeRanks.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task VehicleTreeEntry_ReferencesSameVersionRank_Persists()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var tree = database.AddResearchTree(nation, database.AddVehicleType("Ground"));
        var update = database.AddGameUpdate("2.43");
        var version = database.AddResearchTreeVersion(tree, update);
        var rank = database.AddTreeRank(version, 1);

        var vehicle = new Vehicle { Id = Guid.NewGuid(), Name = "Test Tank" };
        database.Context.Vehicles.Add(vehicle);
        database.AddVehicleTreeEntry(version, vehicle, rank, rpCost: 2900);

        await database.SaveChangesAsync();
        database.Context.ChangeTracker.Clear();

        var entry = await database.Context.VehicleTreeEntries
            .AsNoTracking()
            .Include(e => e.TreeRank)
            .SingleAsync(e => e.VehicleId == vehicle.Id);

        // The invariant that matters: entry and rank share the same version.
        Assert.Equal(version.Id, entry.ResearchTreeVersionId);
        Assert.Equal(version.Id, entry.TreeRank.ResearchTreeVersionId);
        Assert.Equal(1, entry.TreeRank.RankNumber);
    }
}