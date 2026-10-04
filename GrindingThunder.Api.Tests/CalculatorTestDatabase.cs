using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using GrindingThunder.Api.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GrindingThunder.Api.Tests;

internal sealed class CalculatorTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public CalculatorTestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new ApplicationDbContext(options);
        Context.Database.EnsureCreated();
        Calculator = new ResearchCalculatorService(Context);
    }

    public ApplicationDbContext Context { get; }

    public ResearchCalculatorService Calculator { get; }

    public Nation AddNation(string name = "Test Nation")
    {
        var nation = new Nation
        {
            Id = Guid.NewGuid(),
            Name = name
        };

        Context.Nations.Add(nation);
        return nation;
    }

    public VehicleType AddVehicleType(string name)
    {
        var vehicleType = new VehicleType
        {
            Id = Guid.NewGuid(),
            Name = name
        };

        Context.VehicleTypes.Add(vehicleType);
        return vehicleType;
    }

    public ResearchTree AddResearchTree(Nation nation, VehicleType vehicleType)
    {
        var researchTree = new ResearchTree
        {
            Id = Guid.NewGuid(),
            NationId = nation.Id,
            Nation = nation,
            VehicleTypeId = vehicleType.Id,
            VehicleType = vehicleType
        };

        Context.ResearchTrees.Add(researchTree);
        return researchTree;
    }

    public TreeRank AddTreeRank(Nation nation, int rankNumber, int requiredVehiclesUnlocked = 0)
    {
        var researchTree = nation.ResearchTrees.FirstOrDefault();
        if (researchTree is null)
        {
            var groundType = Context.VehicleTypes.Local
                .FirstOrDefault(vt => vt.Name == "Ground")
                ?? AddVehicleType("Ground");

            researchTree = AddResearchTree(nation, groundType);
        }

        return AddTreeRank(researchTree, rankNumber, requiredVehiclesUnlocked);
    }

    public TreeRank AddTreeRank(
        ResearchTree researchTree,
        int rankNumber,
        int requiredVehiclesUnlocked = 0)
    {
        // Rank configuration lives under a version (Batch 4): reuse the
        // tree's existing version, or create one when the tree is new.
        var version = Context.ResearchTreeVersions.Local
            .FirstOrDefault(rtv => rtv.ResearchTreeId == researchTree.Id);

        if (version is null)
        {
            var gameUpdate = Context.GameUpdates.Local.FirstOrDefault()
                ?? AddGameUpdate("test-update");

            version = AddResearchTreeVersion(researchTree, gameUpdate);
        }

        return AddTreeRank(version, rankNumber, requiredVehiclesUnlocked);
    }

    public TreeRank AddTreeRank(
        ResearchTreeVersion version,
        int rankNumber,
        int requiredVehiclesUnlocked = 0)
    {
        var rank = new TreeRank
        {
            Id = Guid.NewGuid(),
            ResearchTreeVersionId = version.Id,
            ResearchTreeVersion = version,
            RankNumber = rankNumber,
            RequiredVehiclesUnlocked = requiredVehiclesUnlocked
        };

        Context.TreeRanks.Add(rank);
        return rank;
    }

    public Vehicle AddVehicle(TreeRank rank, string name, int rpCost)
    {
        return AddVehicle(rank.ResearchTreeVersion.ResearchTree, rank, name, rpCost);
    }

    public Vehicle AddVehicle(
        ResearchTree researchTree,
        TreeRank rank,
        string name,
        int rpCost,
        ResearchTreeVersion? treeVersion = null)
    {
        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            Name = name
        };

        Context.Vehicles.Add(vehicle);

        // A vehicle only exists in a tree through a versioned tree entry
        // carrying its rank and RP cost for that version.
        var version = treeVersion ?? Context.ResearchTreeVersions.Local
            .FirstOrDefault(rtv => rtv.ResearchTreeId == researchTree.Id);

        if (version is null)
        {
            var gameUpdate = Context.GameUpdates.Local.FirstOrDefault()
                ?? AddGameUpdate("test-update");

            version = AddResearchTreeVersion(researchTree, gameUpdate);
        }

        Context.VehicleTreeEntries.Add(new VehicleTreeEntry
        {
            Id = Guid.NewGuid(),
            ResearchTreeVersionId = version.Id,
            ResearchTreeVersion = version,
            VehicleId = vehicle.Id,
            Vehicle = vehicle,
            TreeRankId = ResolveRank(version, rank).Id,
            RpCost = rpCost
        });

        return vehicle;
    }

    public VehicleTreeEntry AddVehicleTreeEntry(
        ResearchTreeVersion version,
        Vehicle vehicle,
        TreeRank rank,
        int rpCost,
        int slCost = 0)
    {
        var resolvedRank = ResolveRank(version, rank);

        var entry = new VehicleTreeEntry
        {
            Id = Guid.NewGuid(),
            ResearchTreeVersionId = version.Id,
            ResearchTreeVersion = version,
            VehicleId = vehicle.Id,
            Vehicle = vehicle,
            TreeRankId = resolvedRank.Id,
            RpCost = rpCost,
            SlCost = slCost
        };

        Context.VehicleTreeEntries.Add(entry);
        return entry;
    }


    /// <summary>
    /// Ensures the entry references a TreeRank of the SAME version (Batch 4
    /// invariant): reuses the given rank when it already belongs to the
    /// version, otherwise maps it by rank number into the target version.
    /// </summary>
    private TreeRank ResolveRank(ResearchTreeVersion version, TreeRank rank)
    {
        if (rank.ResearchTreeVersionId == version.Id)
        {
            return rank;
        }

        var existing = Context.TreeRanks.Local
            .FirstOrDefault(tr => tr.ResearchTreeVersionId == version.Id && tr.RankNumber == rank.RankNumber);
        if (existing is not null)
        {
            return existing;
        }

        return AddTreeRank(version, rank.RankNumber, rank.RequiredVehiclesUnlocked);
    }

    public void AddPrerequisite(Vehicle vehicle, Vehicle prerequisite)
    {
        Context.VehiclePrerequisites.Add(new VehiclePrerequisite
        {
            VehicleId = vehicle.Id,
            Vehicle = vehicle,
            PrerequisiteVehicleId = prerequisite.Id,
            PrerequisiteVehicle = prerequisite
        });
    }

    public GameUpdate AddGameUpdate(
        string version,
        string? name = null,
        DateOnly? releaseDate = null)
    {
        var gameUpdate = new GameUpdate
        {
            Id = Guid.NewGuid(),
            Version = version,
            Name = name,
            ReleaseDate = releaseDate
        };

        Context.GameUpdates.Add(gameUpdate);
        return gameUpdate;
    }

    public ResearchTreeVersion AddResearchTreeVersion(
        ResearchTree researchTree,
        GameUpdate gameUpdate,
        ResearchTreeVersionStatus status = ResearchTreeVersionStatus.Draft)
    {
        var version = new ResearchTreeVersion
        {
            Id = Guid.NewGuid(),
            ResearchTreeId = researchTree.Id,
            ResearchTree = researchTree,
            GameUpdateId = gameUpdate.Id,
            GameUpdate = gameUpdate,
            Status = status
        };

        Context.ResearchTreeVersions.Add(version);
        return version;
    }

    public Task SaveChangesAsync() => Context.SaveChangesAsync();

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
