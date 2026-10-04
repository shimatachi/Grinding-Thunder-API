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

        // 3. Seed one Ground research tree for each standard nation.
        //    Rank configuration is seeded per version in step 4 (Batch 4).
        var nations = await context.Nations
            .Where(n => StandardNations.Contains(n.Name))
            .ToListAsync();

        // Nations that already own a Ground tree are loaded so we can skip
        // them; the tree collection is no longer eagerly loaded just for this.
        var nationIdsWithGroundTree = await context.ResearchTrees
            .Where(rt => rt.VehicleTypeId == groundType.Id)
            .Select(rt => rt.NationId)
            .ToListAsync();
        var nationsWithGroundTree = nationIdsWithGroundTree.ToHashSet();

        foreach (var nation in nations)
        {
            if (nationsWithGroundTree.Contains(nation.Id))
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

            context.ResearchTrees.Add(researchTree);
        }

        await context.SaveChangesAsync();

        // 4. Seed a sample game update and one tree version per research tree.
        //    Development sample data only - real War Thunder update history
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
            var version = new ResearchTreeVersion
            {
                Id = Guid.NewGuid(),
                ResearchTreeId = researchTree.Id,
                ResearchTree = researchTree,
                GameUpdateId = sampleGameUpdate.Id,
                GameUpdate = sampleGameUpdate,
                Status = ResearchTreeVersionStatus.Published
            };

            // Rank configuration is version-specific (Batch 4): each new
            // version receives its own TreeRank rows.
            foreach (var rankNumber in Enumerable.Range(MinRankNumber, MaxRankNumber))
            {
                version.TreeRanks.Add(new TreeRank
                {
                    Id = Guid.NewGuid(),
                    ResearchTreeVersionId = version.Id,
                    ResearchTreeVersion = version,
                    RankNumber = rankNumber,
                    RequiredVehiclesUnlocked = DefaultRequiredVehiclesUnlocked
                });
            }

            context.ResearchTreeVersions.Add(version);
        }

        await context.SaveChangesAsync();

        // 5. Seed sample vehicles, tree entries, and prerequisites if missing.
        if (!await context.VehicleTreeEntries.AnyAsync())
        {
            var usaGroundVersion = await context.ResearchTreeVersions
                .Include(rtv => rtv.TreeRanks)
                .FirstOrDefaultAsync(rtv =>
                    rtv.ResearchTree.Nation.Name == "USA" &&
                    rtv.ResearchTree.VehicleTypeId == groundType.Id &&
                    rtv.Status == ResearchTreeVersionStatus.Published);

            if (usaGroundVersion != null)
            {
                var rank1 = usaGroundVersion.TreeRanks.FirstOrDefault(r => r.RankNumber == 1);
                var rank2 = usaGroundVersion.TreeRanks.FirstOrDefault(r => r.RankNumber == 2);


                if (rank1 != null && rank2 != null)
                {
                    // Stable vehicle identities - version-specific state
                    // (rank, RP cost, SL cost) lives in their tree entries.
                    var m2Light = new Vehicle
                    {
                        Id = Guid.NewGuid(),
                        Name = "M2 Light",
                        ImageUrl = ""
                    };

                    var m3Stuart = new Vehicle
                    {
                        Id = Guid.NewGuid(),
                        Name = "M3 Stuart",
                        ImageUrl = "https://static.encyclopedia.warthunder.com/images/us_m2a4.png"
                    };

                    var m2a4 = new Vehicle
                    {
                        Id = Guid.NewGuid(),
                        Name = "M2A4",
                        ImageUrl = ""
                    };

                    var m3a1Stuart = new Vehicle
                    {
                        Id = Guid.NewGuid(),
                        Name = "M3A1 Stuart",
                        ImageUrl = ""
                    };

                    // Rank II Vehicles
                    var m4a1Sherman = new Vehicle
                    {
                        Id = Guid.NewGuid(),
                        Name = "M4A1 Sherman",
                        ImageUrl = ""
                    };

                    var m3Lee = new Vehicle
                    {
                        Id = Guid.NewGuid(),
                        Name = "M3 Lee",
                        ImageUrl = ""
                    };

                    context.Vehicles.AddRange(m2Light, m3Stuart, m2a4, m3a1Stuart, m4a1Sherman, m3Lee);
                    await context.SaveChangesAsync();

                    // Version-specific placements, including costs and layout.
                    // Local variables let versioned relationships reference entry ids.
                    var m2LightEntry = new VehicleTreeEntry
                    {
                        Id = Guid.NewGuid(),
                        ResearchTreeVersionId = usaGroundVersion.Id,
                        VehicleId = m2Light.Id,
                        TreeRankId = rank1.Id,
                        RpCost = 2900,
                        SlCost = 700,
                        TreeColumn = 1,
                        TreeRow = 1
                    };
                    var m3StuartEntry = new VehicleTreeEntry
                    {
                        Id = Guid.NewGuid(),
                        ResearchTreeVersionId = usaGroundVersion.Id,
                        VehicleId = m3Stuart.Id,
                        TreeRankId = rank1.Id,
                        RpCost = 4000,
                        SlCost = 1400,
                        TreeColumn = 1,
                        TreeRow = 2
                    };
                    var m2a4Entry = new VehicleTreeEntry
                    {
                        Id = Guid.NewGuid(),
                        ResearchTreeVersionId = usaGroundVersion.Id,
                        VehicleId = m2a4.Id,
                        TreeRankId = rank1.Id,
                        RpCost = 2900,
                        SlCost = 700,
                        TreeColumn = 2,
                        TreeRow = 1
                    };
                    var m3a1StuartEntry = new VehicleTreeEntry
                    {
                        Id = Guid.NewGuid(),
                        ResearchTreeVersionId = usaGroundVersion.Id,
                        VehicleId = m3a1Stuart.Id,
                        TreeRankId = rank1.Id,
                        RpCost = 4000,
                        SlCost = 1400,
                        TreeColumn = 2,
                        TreeRow = 2
                    };
                    var m4a1ShermanEntry = new VehicleTreeEntry
                    {
                        Id = Guid.NewGuid(),
                        ResearchTreeVersionId = usaGroundVersion.Id,
                        VehicleId = m4a1Sherman.Id,
                        TreeRankId = rank2.Id,
                        RpCost = 9200,
                        SlCost = 3800,
                        TreeColumn = 1,
                        TreeRow = 1
                    };
                    var m3LeeEntry = new VehicleTreeEntry
                    {
                        Id = Guid.NewGuid(),
                        ResearchTreeVersionId = usaGroundVersion.Id,
                        VehicleId = m3Lee.Id,
                        TreeRankId = rank2.Id,
                        RpCost = 5900,
                        SlCost = 2200,
                        TreeColumn = 2,
                        TreeRow = 1
                    };

                    context.VehicleTreeEntries.AddRange(
                        m2LightEntry, m3StuartEntry, m2a4Entry, m3a1StuartEntry,
                        m4a1ShermanEntry, m3LeeEntry);

                    // Set prerequisite edges. Folder membership is separate and
                    // these sample entries intentionally have no folders.
                    // Version-specific edges (Batch 5): both ends are VehicleTreeEntries in
                    // the same version; Vehicle identities are never referenced directly.
                    context.VehiclePrerequisites.AddRange(
                        new VehiclePrerequisite
                        {
                            VehicleTreeEntryId = m3StuartEntry.Id,
                            PrerequisiteVehicleTreeEntryId = m2LightEntry.Id,
                            ResearchTreeVersionId = usaGroundVersion.Id
                        },
                        new VehiclePrerequisite
                        {
                            VehicleTreeEntryId = m4a1ShermanEntry.Id,
                            PrerequisiteVehicleTreeEntryId = m3StuartEntry.Id,
                            ResearchTreeVersionId = usaGroundVersion.Id
                        },
                        new VehiclePrerequisite
                        {
                            VehicleTreeEntryId = m3a1StuartEntry.Id,
                            PrerequisiteVehicleTreeEntryId = m2a4Entry.Id,
                            ResearchTreeVersionId = usaGroundVersion.Id
                        },
                        new VehiclePrerequisite
                        {
                            VehicleTreeEntryId = m3LeeEntry.Id,
                            PrerequisiteVehicleTreeEntryId = m3a1StuartEntry.Id,
                            ResearchTreeVersionId = usaGroundVersion.Id
                        }
                    );

                    await context.SaveChangesAsync();
                }
            }
        }
    }
}
