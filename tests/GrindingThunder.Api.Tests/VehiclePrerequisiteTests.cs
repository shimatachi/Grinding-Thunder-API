using GrindingThunder.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class VehiclePrerequisiteTests
{
    [Fact]
    public async Task Edges_AreVersionScoped_AndVersionsAreIndependent()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var tree = database.AddResearchTree(nation, database.AddVehicleType("Ground"));
        var updateA = database.AddGameUpdate("2.43");
        var updateB = database.AddGameUpdate("2.45");
        var versionA = database.AddResearchTreeVersion(tree, updateA);
        var versionB = database.AddResearchTreeVersion(tree, updateB);

        var rankA = database.AddTreeRank(versionA, 1);
        var rankB = database.AddTreeRank(versionB, 1);

        // Same two stable vehicles, different edges per version:
        // V1: A -> B, V2: A -> C.
        var a = database.AddVehicle(tree, rankA, "A", 10, treeVersion: versionA);
        var b = database.AddVehicle(tree, rankA, "B", 20, treeVersion: versionA);
        var c = database.AddVehicle(tree, rankA, "C", 30, treeVersion: versionA);
        database.AddVehicleTreeEntry(versionB, a, rankB, 10);
        database.AddVehicleTreeEntry(versionB, c, rankB, 30);

        database.AddPrerequisite(b, a, versionA);
        database.AddPrerequisite(c, a, versionB);
        await database.SaveChangesAsync();

        var edges = await database.Context.VehiclePrerequisites.AsNoTracking().ToListAsync();

        Assert.Equal(2, edges.Count);
        Assert.All(edges, e =>
            Assert.NotEqual(e.VehicleTreeEntryId, e.PrerequisiteVehicleTreeEntryId));
        // Both edges carry their own version id (denormalized invariant column).
        Assert.Equal(2, edges.Select(e => e.ResearchTreeVersionId).Distinct().Count());
    }

    [Fact]
    public async Task DuplicateEdge_BetweenSameEntries_IsRejected()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var tree = database.AddResearchTree(nation, database.AddVehicleType("Ground"));
        var version = database.AddResearchTreeVersion(tree, database.AddGameUpdate("2.43"));
        var rank = database.AddTreeRank(version, 1);

        var a = database.AddVehicle(tree, rank, "A", 10, treeVersion: version);
        var b = database.AddVehicle(tree, rank, "B", 20, treeVersion: version);

        database.AddPrerequisite(b, a, version);
        await database.SaveChangesAsync();

        // Second identical edge: rejected by the composite primary key.
        var existing = await database.Context.VehiclePrerequisites.AsNoTracking().SingleAsync();
        // EF's identity map rejects the duplicate as soon as it is tracked;
        // the composite primary key rejects it at the DB level when the
        // change tracker is bypassed. Either path is a rejection - what
        // matters is that a duplicate edge can never be saved.
        Assert.Throws<InvalidOperationException>(() =>
            database.Context.VehiclePrerequisites.Add(new VehiclePrerequisite
            {
                VehicleTreeEntryId = existing.VehicleTreeEntryId,
                PrerequisiteVehicleTreeEntryId = existing.PrerequisiteVehicleTreeEntryId,
                ResearchTreeVersionId = existing.ResearchTreeVersionId
            }));
    }

    [Fact]
    public async Task SelfEdge_IsRejected()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var tree = database.AddResearchTree(nation, database.AddVehicleType("Ground"));
        var version = database.AddResearchTreeVersion(tree, database.AddGameUpdate("2.43"));
        var rank = database.AddTreeRank(version, 1);

        var a = database.AddVehicle(tree, rank, "A", 10, treeVersion: version);
        await database.SaveChangesAsync();

        database.Context.VehiclePrerequisites.Add(new VehiclePrerequisite
        {
            VehicleTreeEntryId = (await database.Context.VehicleTreeEntries
                .SingleAsync(e => e.VehicleId == a.Id)).Id,
            PrerequisiteVehicleTreeEntryId = (await database.Context.VehicleTreeEntries
                .SingleAsync(e => e.VehicleId == a.Id)).Id,
            ResearchTreeVersionId = version.Id
        });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => database.SaveChangesAsync());
    }

    [Fact]
    public async Task CrossVersionEdge_IsRejected()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var tree = database.AddResearchTree(nation, database.AddVehicleType("Ground"));
        var updateA = database.AddGameUpdate("2.43");
        var updateB = database.AddGameUpdate("2.45");
        var versionA = database.AddResearchTreeVersion(tree, updateA);
        var versionB = database.AddResearchTreeVersion(tree, updateB);
        var rankA = database.AddTreeRank(versionA, 1);
        var rankB = database.AddTreeRank(versionB, 1);

        var a = database.AddVehicle(tree, rankA, "A", 10, treeVersion: versionA);
        var b = database.AddVehicle(tree, rankB, "B", 20, treeVersion: versionB);
        await database.SaveChangesAsync();

        var entryA = await database.Context.VehicleTreeEntries
            .SingleAsync(e => e.VehicleId == a.Id && e.ResearchTreeVersionId == versionA.Id);
        var entryB = await database.Context.VehicleTreeEntries
            .SingleAsync(e => e.VehicleId == b.Id && e.ResearchTreeVersionId == versionB.Id);

        // Same-version invariant column says versionA, but the prerequisite
        // entry lives in versionB: the composite FKs cannot both match, so the
        // database must reject the insert.
        database.Context.VehiclePrerequisites.Add(new VehiclePrerequisite
        {
            VehicleTreeEntryId = entryA.Id,
            PrerequisiteVehicleTreeEntryId = entryB.Id,
            ResearchTreeVersionId = versionA.Id
        });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => database.SaveChangesAsync());
    }
}
