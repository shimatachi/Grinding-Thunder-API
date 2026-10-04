using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class ResearchTreeVersionPersistenceTests
{
    [Fact]
    public async Task DbInitializer_SeedsSampleGameUpdateAndVersionPerTree()
    {
        using var database = new CalculatorTestDatabase();

        await DbInitializer.InitializeAsync(database.Context);

        var gameUpdate = await database.Context.GameUpdates
            .AsNoTracking()
            .SingleAsync(gu => gu.Version == "dev-sample");
        Assert.Equal("Sample Dev Update", gameUpdate.Name);
        Assert.NotNull(gameUpdate.ReleaseDate);

        var versions = await database.Context.ResearchTreeVersions
            .AsNoTracking()
            .ToListAsync();
        var trees = await database.Context.ResearchTrees
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(trees.Count, versions.Count);
        Assert.All(versions, version =>
        {
            Assert.Equal(gameUpdate.Id, version.GameUpdateId);
            Assert.Equal(ResearchTreeVersionStatus.Published, version.Status);
        });
        Assert.Equal(
            trees.Select(tree => tree.Id).OrderBy(id => id),
            versions.Select(version => version.ResearchTreeId).OrderBy(id => id));
    }

    [Fact]
    public async Task ResearchTree_CanHaveVersionsForMultipleGameUpdates()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var ground = database.AddVehicleType("Ground");
        var usaGroundTree = database.AddResearchTree(nation, ground);
        var updateA = database.AddGameUpdate("2.43", "Sky Guardians");
        var updateB = database.AddGameUpdate("2.45", name: null);
        database.AddResearchTreeVersion(usaGroundTree, updateA);
        database.AddResearchTreeVersion(usaGroundTree, updateB);

        await database.SaveChangesAsync();
        database.Context.ChangeTracker.Clear();

        var persisted = await database.Context.ResearchTrees
            .AsNoTracking()
            .Include(rt => rt.Versions)
            .SingleAsync(rt => rt.Id == usaGroundTree.Id);

        Assert.Equal(2, persisted.Versions.Count);
    }

    [Fact]
    public async Task GameUpdate_CanHaveVersionsForMultipleResearchTrees()
    {
        using var database = new CalculatorTestDatabase();
        var usa = database.AddNation("USA");
        var germany = database.AddNation("Germany");
        var ground = database.AddVehicleType("Ground");
        var usaGroundTree = database.AddResearchTree(usa, ground);
        var germanyGroundTree = database.AddResearchTree(germany, ground);
        var updateA = database.AddGameUpdate("2.43", "Sky Guardians");
        database.AddResearchTreeVersion(usaGroundTree, updateA);
        database.AddResearchTreeVersion(germanyGroundTree, updateA);

        await database.SaveChangesAsync();
        database.Context.ChangeTracker.Clear();

        var persisted = await database.Context.GameUpdates
            .AsNoTracking()
            .Include(gu => gu.ResearchTreeVersions)
            .SingleAsync(gu => gu.Id == updateA.Id);

        Assert.Equal(2, persisted.ResearchTreeVersions.Count);
    }

    [Fact]
    public async Task SameResearchTreeAndGameUpdatePair_CannotBeDuplicated()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var ground = database.AddVehicleType("Ground");
        var researchTree = database.AddResearchTree(nation, ground);
        var update = database.AddGameUpdate("2.43");
        database.AddResearchTreeVersion(researchTree, update);
        await database.SaveChangesAsync();

        database.Context.ChangeTracker.Clear();
        database.AddResearchTreeVersion(researchTree, update);

        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
    }

    [Fact]
    public async Task DuplicateGameUpdateVersions_CannotBePersisted()
    {
        using var database = new CalculatorTestDatabase();
        database.AddGameUpdate("2.43", "Sky Guardians");
        database.AddGameUpdate("2.43", "Impostor update");

        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
    }

    [Fact]
    public async Task DeletingResearchTree_CascadesItsVersions()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var ground = database.AddVehicleType("Ground");
        var researchTree = database.AddResearchTree(nation, ground);
        var update = database.AddGameUpdate("2.43");
        database.AddResearchTreeVersion(researchTree, update);
        await database.SaveChangesAsync();

        database.Context.ChangeTracker.Clear();
        database.Context.ResearchTrees.Remove(
            await database.Context.ResearchTrees.SingleAsync(rt => rt.Id == researchTree.Id));
        await database.SaveChangesAsync();

        var remainingVersions = await database.Context.ResearchTreeVersions
            .AsNoTracking()
            .CountAsync();
        Assert.Equal(0, remainingVersions);
        Assert.Equal(
            1,
            await database.Context.GameUpdates.AsNoTracking().CountAsync(gu => gu.Id == update.Id));
    }

    [Fact]
    public async Task DeletingGameUpdate_CascadesItsVersions()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var ground = database.AddVehicleType("Ground");
        var usaGroundTree = database.AddResearchTree(nation, ground);
        var update = database.AddGameUpdate("2.43");
        database.AddResearchTreeVersion(usaGroundTree, update);
        await database.SaveChangesAsync();

        database.Context.ChangeTracker.Clear();
        database.Context.GameUpdates.Remove(update);
        await database.SaveChangesAsync();

        var remainingVersions = await database.Context.ResearchTreeVersions
            .AsNoTracking()
            .CountAsync();
        Assert.Equal(0, remainingVersions);
        Assert.Equal(
            1,
            await database.Context.ResearchTrees.AsNoTracking().CountAsync(rt => rt.Id == usaGroundTree.Id));
    }

    [Fact]
    public async Task VersionStatus_PersistsAsExpected()
    {
        using var database = new CalculatorTestDatabase();
        var nation = database.AddNation("USA");
        var ground = database.AddVehicleType("Ground");
        var aviation = database.AddVehicleType("Aviation");
        var researchTree = database.AddResearchTree(nation, ground);
        var update = database.AddGameUpdate("2.43");
        var draft = database.AddResearchTreeVersion(researchTree, update, ResearchTreeVersionStatus.Draft);
        var published = database.AddResearchTreeVersion(
            database.AddResearchTree(nation, aviation),
            update,
            ResearchTreeVersionStatus.Published);

        await database.SaveChangesAsync();
        database.Context.ChangeTracker.Clear();

        var persistedDraft = await database.Context.ResearchTreeVersions
            .AsNoTracking()
            .SingleAsync(rtv => rtv.Id == draft.Id);
        var persistedPublished = await database.Context.ResearchTreeVersions
            .AsNoTracking()
            .SingleAsync(rtv => rtv.Id == published.Id);

        Assert.Equal(ResearchTreeVersionStatus.Draft, persistedDraft.Status);
        Assert.Equal(ResearchTreeVersionStatus.Published, persistedPublished.Status);
    }
}
