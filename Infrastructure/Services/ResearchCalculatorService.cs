using Microsoft.EntityFrameworkCore;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Domain.Services;
using GrindingThunder.Api.Infrastructure.Persistence;

namespace GrindingThunder.Api.Infrastructure.Services;

/// <summary>
/// Calculates the research points (RP) required to unlock a target vehicle by
/// traversing the explicit prerequisite lines upwards and incorporating
/// player-chosen filler vehicles for rank-gate requirements.
/// No auto-picking of fillers ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â the frontend supplies explicit FillerTargetIds.
/// </summary>
public class ResearchCalculatorService : IResearchCalculatorService
{
    private readonly ApplicationDbContext _context;

    public ResearchCalculatorService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ResearchCalculationResult> CalculateResearchAsync(
        ResearchCalculationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.AverageRpPerMatch <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.AverageRpPerMatch),
                "Average RP per match must be greater than zero.");
        }

        var unlockedSet = (request.UnlockedVehicleIds ?? new List<Guid>())
            .Distinct()
            .ToHashSet();

        var fillerTargetIds = (request.FillerTargetIds ?? new List<Guid>())
            .Distinct()
            .ToHashSet();

        // Locate the target vehicle's tree entry and its research tree so we can scope the traversal.
        // TRANSITIONAL (Batch 3): the calculation request is not yet
        // version-aware (that redesign belongs to Batch 7). The target's tree
        // is resolved through VehicleTreeEntries; RP cost and rank are read
        // from the entry, not from the Vehicle itself.
        var targetEntries = await _context.VehicleTreeEntries
            .AsNoTracking()
            .Include(e => e.Rank)
            .Where(e => e.VehicleId == request.TargetVehicleId)
            .ToListAsync(cancellationToken);

        if (targetEntries.Count == 0)
        {
            throw new KeyNotFoundException(
                $"Vehicle '{request.TargetVehicleId}' was not found in any research tree version.");
        }

        if (targetEntries.Select(e => e.ResearchTreeVersionId).Distinct().Count() > 1)
        {
            // Fail clearly instead of silently choosing an arbitrary version.
            throw new InvalidOperationException(
                $"Vehicle '{request.TargetVehicleId}' appears in multiple research tree versions; " +
                "version-aware calculation is not implemented yet.");
        }

        var targetEntry = targetEntries[0];
        var researchTreeId = targetEntry.Rank.ResearchTreeId;

        // Load the full research sub-graph for the target's tree in a few queries,
        // then traverse in memory to avoid N+1 async round trips.
        var entries = await _context.VehicleTreeEntries
            .AsNoTracking()
            .Include(e => e.Vehicle)
            .Where(e => e.Rank.ResearchTreeId == researchTreeId)
            .ToListAsync(cancellationToken);

        var vehicles = entries.Select(e => e.Vehicle).ToList();
        // TRANSITIONAL (Batch 3): the request is not version-aware yet (Batch 7).
        // If any vehicle in the tree — not just the target — appears in multiple
        // versions, the one-entry-per-vehicle assumption breaks. Fail with a clear
        // domain error instead of an opaque dictionary failure.
        var ambiguousVehicleId = entries
            .GroupBy(e => e.VehicleId)
            .FirstOrDefault(g => g.Count() > 1)?.Key;

        if (ambiguousVehicleId.HasValue)
        {
            throw new InvalidOperationException(
                $"Vehicle '{ambiguousVehicleId.Value}' appears in multiple research tree versions; " +
                "version-aware calculation is not implemented yet.");
        }

        var entryByVehicleId = entries.ToDictionary(e => e.VehicleId);

        var vehicleIds = vehicles.Select(v => v.Id).ToList();

        var prerequisites = await _context.VehiclePrerequisites
            .AsNoTracking()
            .Where(vp => vehicleIds.Contains(vp.VehicleId) && vehicleIds.Contains(vp.PrerequisiteVehicleId))
            .ToListAsync(cancellationToken);

        var ranks = await _context.Ranks
            .AsNoTracking()
            .Where(r => r.ResearchTreeId == researchTreeId)
            .ToListAsync(cancellationToken);

        var vehicleMap = vehicles.ToDictionary(v => v.Id);
        var vehiclesByRank = entries.GroupBy(e => e.RankId).ToDictionary(g => g.Key, g => g.Select(e => e.Vehicle).ToList());

        // VehicleId -> list of prerequisite vehicle ids (dependency direction points up the tree).
        var prereqMap = prerequisites
            .GroupBy(vp => vp.VehicleId)
            .ToDictionary(g => g.Key, g => g.Select(vp => vp.PrerequisiteVehicleId).ToList());

        // ------------------------------------------------------------------
        // Step 1: Direct line traversal (upward graph search).
        //
        // Starting from the target vehicle, recursively collect every mandatory
        // parent defined by the explicit prerequisite edges. A branch stops as
        // soon as a vehicle has no prerequisites (e.g. a broken column line at
        // Rank IV). Vehicles already present in UnlockedVehicleIds are excluded
        // from the required set.
        // ------------------------------------------------------------------
        var required = new List<Vehicle>();
        var requiredIds = new HashSet<Guid>();
        var visited = new HashSet<Guid>();

        void CollectLine(Guid vehicleId)
        {
            // Unknown vehicle (outside the nation graph) or already traversed.
            if (!vehicleMap.TryGetValue(vehicleId, out var vehicle) || !visited.Add(vehicleId))
            {
                return;
            }

            if (unlockedSet.Contains(vehicleId)) return;

            if (!unlockedSet.Contains(vehicleId))
            {
                requiredIds.Add(vehicleId);
                required.Add(vehicle);
            }

            // No prerequisites -> the line is broken and traversal stops here.
            if (prereqMap.TryGetValue(vehicleId, out var prereqIds))
            {
                foreach (var prereqId in prereqIds)
                {
                    CollectLine(prereqId);
                }
            }
        }

        CollectLine(targetEntry.VehicleId);

        // ------------------------------------------------------------------
        // Step 1b: Collect explicit filler targets and their line prerequisites.
        //
        // Each player-chosen filler vehicle is added along with its full
        // prerequisite chain. Filler prereqs that overlap with the mandatory
        // line (requiredIds) are kept as mandatory (not double-counted).
        // ------------------------------------------------------------------
        var fillerIds = new HashSet<Guid>();
        var fillerVehicles = new List<Vehicle>();

        void CollectFillerLine(Guid vehicleId)
        {
            if (!vehicleMap.TryGetValue(vehicleId, out var vehicle))
                return;

            // Already unlocked ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â skip.
            if (unlockedSet.Contains(vehicleId))
                return;

            // Already part of the mandatory line ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â no need to also track as filler.
            if (requiredIds.Contains(vehicleId))
            {
                // Still recurse into prerequisites in case they're not in the line.
                if (prereqMap.TryGetValue(vehicleId, out var prereqIds))
                {
                    foreach (var prereqId in prereqIds)
                    {
                        CollectFillerLine(prereqId);
                    }
                }
                return;
            }

            // Already collected as filler ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â avoid duplicate work.
            if (!fillerIds.Add(vehicleId))
                return;

            fillerVehicles.Add(vehicle);

            // Recurse into prerequisites.
            if (prereqMap.TryGetValue(vehicleId, out var fillerPrereqIds))
            {
                foreach (var prereqId in fillerPrereqIds)
                {
                    CollectFillerLine(prereqId);
                }
            }
        }

        foreach (var fillerId in fillerTargetIds)
        {
            CollectFillerLine(fillerId);
        }

        // ------------------------------------------------------------------
        // Step 2: Rank gate verification (Rank I up to TargetRank - 1).
        //
        // Evaluate each rank sequentially. Count unique vehicles that are
        // either already unlocked, part of the mandatory line, or part of the
        // explicit filler set. If a rank's quota is not met, STOP ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â return
        // the accumulated results with a RankDeficit for that rank.
        // Do NOT auto-pick fillers.
        // ------------------------------------------------------------------
        var targetRankNumber = targetEntry.Rank.RankNumber;
        var rankDeficits = new List<RankDeficitDto>();

        foreach (var rank in ranks
            .Where(r => r.RankNumber < targetRankNumber)
            .OrderBy(r => r.RankNumber))
        {
            var threshold = rank.RequiredVehiclesUnlocked;
            if (threshold <= 0)
            {
                continue;
            }

            var rankVehicles = vehiclesByRank.TryGetValue(rank.Id, out var rv) ? rv : new List<Vehicle>();

            var count = rankVehicles.Count(v =>
                unlockedSet.Contains(v.Id)
                || requiredIds.Contains(v.Id)
                || fillerIds.Contains(v.Id));

            var shortfall = threshold - count;
            if (shortfall > 0)
            {
                // Rank gate not satisfied ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â report deficit and stop.
                rankDeficits.Add(new RankDeficitDto(
                    rank.RankNumber,
                    shortfall,
                    threshold));

                // Stop evaluating further ranks ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â the player must resolve
                // this deficit before higher ranks can be calculated.
                break;
            }
        }

        // ------------------------------------------------------------------
        // Step 3: Total RP = mandatory line vehicles + explicit fillers.
        // ------------------------------------------------------------------
        var requiredVehicleSummaries = new List<VehicleRpSummary>(required.Count + fillerVehicles.Count);
        requiredVehicleSummaries.AddRange(required.Select(v =>
            new VehicleRpSummary(v.Id, v.Name, entryByVehicleId[v.Id].RpCost, false)));
        requiredVehicleSummaries.AddRange(fillerVehicles.Select(v =>
            new VehicleRpSummary(v.Id, v.Name, entryByVehicleId[v.Id].RpCost, true)));

        var totalRp = requiredVehicleSummaries.Sum(v => v.RpRemaining);
        var estimatedMatches = (int)Math.Ceiling((double)totalRp / request.AverageRpPerMatch);

        return new ResearchCalculationResult(
            targetEntry.VehicleId,
            totalRp,
            estimatedMatches,
            requiredVehicleSummaries,
            rankDeficits);
    }
}
