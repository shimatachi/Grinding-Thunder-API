using Microsoft.EntityFrameworkCore;
using GrindingThunder.Api.Domain.Entities;

namespace GrindingThunder.Api.Infrastructure.Persistence;

public static class DbInitializer
{
    private static readonly string[] StandardVehicleTypes =
    {
        "Ground",
        "Aviation",
        "Helicopter",
        "Coastal Fleet",
        "Bluewater Fleet"
    };

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
    private const string SampleGameUpdateVersion = "dev-sample";

    public static async Task InitializeAsync(ApplicationDbContext context)
    {
        // 1. Seed vehicle types if missing.
        var vehicleTypes = await context.VehicleTypes.ToListAsync();
        var vehicleTypesByName = vehicleTypes.ToDictionary(vt => vt.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var vehicleTypeName in StandardVehicleTypes)
        {
            if (vehicleTypesByName.ContainsKey(vehicleTypeName))
            {
                continue;
            }

            var vehicleType = new VehicleType
            {
                Id = Guid.NewGuid(),
                Name = vehicleTypeName
            };

            context.VehicleTypes.Add(vehicleType);
            vehicleTypesByName.Add(vehicleTypeName, vehicleType);
        }

        await context.SaveChangesAsync();
        var groundType = vehicleTypesByName["Ground"];

        // 2. Seed nations if missing.
        if (!await context.Nations.AnyAsync())
        {
            foreach (var nationName in StandardNations)
            {
                var nation = new Nation
                {
                    Id = Guid.NewGuid(),
                    Name = nationName
                };

                context.Nations.Add(nation);
            }

            await context.SaveChangesAsync();
        }

        // 3. Seed one Ground research tree and its ranks for each standard nation.
        var nations = await context.Nations
            .Where(n => StandardNations.Contains(n.Name))
            .Include(n => n.ResearchTrees)
            .ThenInclude(rt => rt.Ranks)
            .ToListAsync();

        foreach (var nation in nations)
        {
            if (nation.ResearchTrees.Any(rt => rt.VehicleTypeId == groundType.Id))
            {
                continue;
            }

            var researchTree = new ResearchTree
            {
                Id = Guid.NewGuid(),
                NationId = nation.Id,
                Nation = nation,
                VehicleTypeId = groundType.Id,
                VehicleType = groundType
            };

            foreach (var rankNumber in Enumerable.Range(MinRankNumber, MaxRankNumber))
            {
                researchTree.Ranks.Add(new Rank
                {
                    Id = Guid.NewGuid(),
                    ResearchTreeId = researchTree.Id,
                    ResearchTree = researchTree,
                    RankNumber = rankNumber,
                    RequiredVehiclesUnlocked = DefaultRequiredVehiclesUnlocked
                });
            }

            context.ResearchTrees.Add(researchTree);
        }

        await context.SaveChangesAsync();

        // 4. Seed a sample game update and one tree version per research tree.
        //    Development sample data only — real War Thunder update history
        //    is intentionally not modeled yet.
        var sampleGameUpdate = await context.GameUpdates
            .FirstOrDefaultAsync(gu => gu.Version == SampleGameUpdateVersion);

        if (sampleGameUpdate is null)
        {
            sampleGameUpdate = new GameUpdate
            {
                Id = Guid.NewGuid(),
                Version = SampleGameUpdateVersion,
                Name = "Sample Dev Update",
                ReleaseDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };

            context.GameUpdates.Add(sampleGameUpdate);
            await context.SaveChangesAsync();
        }

        var treesWithoutSampleVersion = await context.ResearchTrees
            .Where(rt => !context.ResearchTreeVersions.Any(rtv =>
                rtv.ResearchTreeId == rt.Id &&
                rtv.GameUpdateId == sampleGameUpdate.Id))
            .ToListAsync();

        foreach (var researchTree in treesWithoutSampleVersion)
        {
            context.ResearchTreeVersions.Add(new ResearchTreeVersion
            {
                Id = Guid.NewGuid(),
                ResearchTreeId = researchTree.Id,
                ResearchTree = researchTree,
                GameUpdateId = sampleGameUpdate.Id,
                GameUpdate = sampleGameUpdate,
                Status = ResearchTreeVersionStatus.Published
            });
        }

        await context.SaveChangesAsync();

        // 5. Seed sample vehicles and prerequisites if missing.
        if (!await context.Vehicles.AnyAsync())
        {
            var usaGroundTree = await context.ResearchTrees
                .Include(rt => rt.Ranks)
                .FirstOrDefaultAsync(rt =>
                    rt.Nation.Name == "USA" && rt.VehicleTypeId == groundType.Id);

            if (usaGroundTree != null)
            {
                var rank1 = usaGroundTree.Ranks.FirstOrDefault(r => r.RankNumber == 1);
                var rank2 = usaGroundTree.Ranks.FirstOrDefault(r => r.RankNumber == 2);

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
