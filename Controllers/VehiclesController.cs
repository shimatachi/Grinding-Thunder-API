using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrindingThunder.Api.Infrastructure.Persistence;

namespace GrindingThunder.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VehiclesController(ApplicationDbContext context) : ControllerBase
{
    private readonly ApplicationDbContext _context = context;

    [HttpGet]
    public async Task<IActionResult> GetVehicles()
    {
        // TRANSITIONAL (Batch 3): rank/RP/SL now live on VehicleTreeEntry.
        // Until explicit version selection exists (Batch 7), a vehicle's flat
        // RankId/RpCost/SlCost fields are only populated when it has exactly
        // one tree entry; they are null when it has none or spans multiple
        // versions. We never silently pick an arbitrary entry as canonical.
        var vehicles = await _context.Vehicles
            .AsNoTracking()
            .Select(v => new
            {
                v.Id,
                TreeEntries = v.TreeEntries.Select(e => new
                {
                    e.ResearchTreeVersionId,
                    e.TreeRankId,
                    RankNumber = (int?)e.TreeRank.RankNumber,
                    e.RpCost,
                    e.SlCost
                }).ToList(),
                v.Name,
                v.ImageUrl,
                v.IsFolderParent,
                v.FolderParentId,
                v.TreeColumn,
                v.TreeRow,
                Prerequisites = v.Prerequisites.Select(p => p.PrerequisiteVehicleId).ToList()
            })
            .ToListAsync();

        return Ok(vehicles.Select(v =>
        {
            // Deterministic: only expose flat values when there is exactly one entry.
            var singleEntry = v.TreeEntries.Count == 1 ? v.TreeEntries[0] : null;

            return new
            {
                v.Id,
                RankId = singleEntry?.TreeRankId,
                v.Name,
                v.ImageUrl,
                RpCost = singleEntry?.RpCost,
                SlCost = singleEntry?.SlCost,
                v.IsFolderParent,
                v.FolderParentId,
                v.TreeColumn,
                v.TreeRow,
                v.Prerequisites,
                TreeEntries = v.TreeEntries
            };
        }));
    }

    [HttpGet("tree")]
    public async Task<IActionResult> GetVehicleTree(
        [FromQuery] Guid nationId,
        [FromQuery] string? type,
        CancellationToken cancellationToken)
    {
        if (nationId == Guid.Empty)
        {
            return BadRequest("nationId is required.");
        }

                // TRANSITIONAL (Batch 3): rank/RP/SL now live on VehicleTreeEntry; the
        // tree is filtered through the entry's rank and projected from the entry.
        var query = _context.VehicleTreeEntries
            .AsNoTracking()
            .Where(e => e.TreeRank.ResearchTreeVersion.ResearchTree.NationId == nationId);

        if (!string.IsNullOrWhiteSpace(type))
        {
            var normalizedType = type.ToLowerInvariant();
            query = query.Where(e => e.TreeRank.ResearchTreeVersion.ResearchTree.VehicleType.Name.ToLower() == normalizedType);
        }

        var entries = await query
            .Select(e => new
            {
                e.Vehicle.Id,
                e.TreeRankId,
                e.TreeRank.ResearchTreeVersionId,
                e.Vehicle.Name,
                e.Vehicle.ImageUrl,
                e.RpCost,
                e.SlCost,
                e.Vehicle.IsFolderParent,
                e.Vehicle.FolderParentId,
                e.Vehicle.TreeColumn,
                e.Vehicle.TreeRow,
                PrerequisiteIds = e.Vehicle.Prerequisites
                    .Select(p => p.PrerequisiteVehicleId)
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return Ok(entries);
    }
}
