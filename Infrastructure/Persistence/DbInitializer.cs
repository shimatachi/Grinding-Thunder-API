using Microsoft.EntityFrameworkCore;
using GrindingThunder.Api.Domain.Entities;

namespace GrindingThunder.Api.Infrastructure.Persistence;

public static class DbInitializer
{
    private static readonly string[] StandardNations =
    {
        "USA",
        "USSR",
        "Germany",
        "Great Britain"
    };

    private const int MinRankNumber = 1;
    private const int MaxRankNumber = 8;
    private const int DefaultRequiredVehiclesUnlocked = 5;

    public static async Task InitializeAsync(ApplicationDbContext context)
    {
        // 1. Seed Nations & Ranks if missing
        if (!await context.Nations.AnyAsync())
        {
            foreach (var nationName in StandardNations)
            {
                var nation = new Nation
                {
                    Id = Guid.NewGuid(),
                    Name = nationName,
                    Type = "Ground",
                    Ranks = Enumerable.Range(MinRankNumber, MaxRankNumber)
                        .Select(rankNumber => new Rank
                        {
                            Id = Guid.NewGuid(),
                            RankNumber = rankNumber,
                            RequiredVehiclesUnlocked = DefaultRequiredVehiclesUnlocked
                        })
                        .ToList()
                };

                context.Nations.Add(nation);
            }

            await context.SaveChangesAsync();
        }

        // 2. Seed Sample Vehicles & Prerequisites if missing
        if (!await context.Vehicles.AnyAsync())
        {
            var usa = await context.Nations
                .Include(n => n.Ranks)
                .FirstOrDefaultAsync(n => n.Name == "USA");

            if (usa != null)
            {
                var rank1 = usa.Ranks.FirstOrDefault(r => r.RankNumber == 1);
                var rank2 = usa.Ranks.FirstOrDefault(r => r.RankNumber == 2);

                if (rank1 != null && rank2 != null)
                {
                    // Rank I Vehicles
                    var m2Light = new Vehicle
                    {
                        Id = Guid.NewGuid(),
                        RankId = rank1.Id,
                        Name = "M2 Light",
                        RpCost = 2900,
                        SlCost = 700,
                        IsFolderParent = false,
                        TreeColumn = 1,
                        TreeRow = 1,
                        ImageUrl = ""
                    };

                    var m3Stuart = new Vehicle
                    {
                        Id = Guid.NewGuid(),
                        RankId = rank1.Id,
                        Name = "M3 Stuart",
                        RpCost = 4000,
                        SlCost = 1400,
                        IsFolderParent = false,
                        TreeColumn = 1,
                        TreeRow = 2,
                        ImageUrl = "https://static.encyclopedia.warthunder.com/images/us_m2a4.png"
                    };

                    var m2a4 = new Vehicle
                    {
                        Id = Guid.NewGuid(),
                        RankId = rank1.Id,
                        Name = "M2A4",
                        RpCost = 2900,
                        SlCost = 700,
                        IsFolderParent = false,
                        TreeColumn = 2,
                        TreeRow = 1,
                        ImageUrl = ""
                    };

                    var m3a1Stuart = new Vehicle
                    {
                        Id = Guid.NewGuid(),
                        RankId = rank1.Id,
                        Name = "M3A1 Stuart",
                        RpCost = 4000,
                        SlCost = 1400,
                        IsFolderParent = false,
                        TreeColumn = 2,
                        TreeRow = 2,
                        ImageUrl = ""
                    };

                    // Rank II Vehicles
                    var m4a1Sherman = new Vehicle
                    {
                        Id = Guid.NewGuid(),
                        RankId = rank2.Id,
                        Name = "M4A1 Sherman",
                        RpCost = 9200,
                        SlCost = 3800,
                        IsFolderParent = false,
                        TreeColumn = 1,
                        TreeRow = 1,
                        ImageUrl = ""
                    };

                    var m3Lee = new Vehicle
                    {
                        Id = Guid.NewGuid(),
                        RankId = rank2.Id,
                        Name = "M3 Lee",
                        RpCost = 5900,
                        SlCost = 2200,
                        IsFolderParent = false,
                        TreeColumn = 2,
                        TreeRow = 1,
                        ImageUrl = ""
                    };

                    context.Vehicles.AddRange(m2Light, m3Stuart, m2a4, m3a1Stuart, m4a1Sherman, m3Lee);

                    // Set Prerequisite Edges
                    context.VehiclePrerequisites.AddRange(
                        new VehiclePrerequisite
                        {
                            VehicleId = m3Stuart.Id,
                            PrerequisiteVehicleId = m2Light.Id
                        },
                        new VehiclePrerequisite
                        {
                            VehicleId = m4a1Sherman.Id,
                            PrerequisiteVehicleId = m3Stuart.Id
                        },
                        new VehiclePrerequisite
                        {
                            VehicleId = m3a1Stuart.Id,
                            PrerequisiteVehicleId = m2a4.Id
                        },
                        new VehiclePrerequisite
                        {
                            VehicleId = m3Lee.Id,
                            PrerequisiteVehicleId = m3a1Stuart.Id
                        }
                    );

                    await context.SaveChangesAsync();
                }
            }
        }
    }
}