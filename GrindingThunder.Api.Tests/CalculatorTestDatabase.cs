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

    public Rank AddRank(Nation nation, int rankNumber, int requiredVehiclesUnlocked = 0)
    {
        var researchTree = nation.ResearchTrees.FirstOrDefault();
        if (researchTree is null)
        {
            var groundType = Context.VehicleTypes.Local
                .FirstOrDefault(vt => vt.Name == "Ground")
                ?? AddVehicleType("Ground");

            researchTree = AddResearchTree(nation, groundType);
        }

        return AddRank(researchTree, rankNumber, requiredVehiclesUnlocked);
    }

    public Rank AddRank(
        ResearchTree researchTree,
        int rankNumber,
        int requiredVehiclesUnlocked = 0)
    {
        var rank = new Rank
        {
            Id = Guid.NewGuid(),
            ResearchTreeId = researchTree.Id,
            ResearchTree = researchTree,
            RankNumber = rankNumber,
            RequiredVehiclesUnlocked = requiredVehiclesUnlocked
        };

        Context.Ranks.Add(rank);
        return rank;
    }

    public Vehicle AddVehicle(Rank rank, string name, int rpCost)
    {
        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            RankId = rank.Id,
            Rank = rank,
            Name = name,
            RpCost = rpCost
        };

        Context.Vehicles.Add(vehicle);
        return vehicle;
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
