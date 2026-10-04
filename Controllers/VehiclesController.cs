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
        // TRANSITIONAL (Batches 3-6): all versioned values are projected from
        // VehicleTreeEntry. Until explicit version selection exists (Batch 7),
        // flat values are populated only when a vehicle has exactly one entry.
        var vehicles = await _context.Vehicles
            .AsNoTracking()
            .AsSplitQuery()
            .Select(v => new
            {
                v.Id,
                TreeEntries = v.TreeEntries.Select(e => new
                {
                    e.ResearchTreeVersionId,
                    e.TreeRankId,
                    RankNumber = (int?)e.TreeRank.RankNumber,
                    e.RpCost,
                    e.SlCost,
                    e.TreeColumn,
                    e.TreeRow,
                    FolderParentId = e.FolderParentEntry == null
                        ? (Guid?)null
                        : e.FolderParentEntry.VehicleId,
                    IsFolderParent = e.FolderChildren.Any(),
                    PrerequisiteIds = e.Prerequisites
                        .Select(pr => pr.PrerequisiteVehicleTreeEntryId)
                        .ToList()
                }).ToList(),
                v.Name,
                v.ImageUrl,
                PrerequisiteEntryIds = v.TreeEntries
                    .SelectMany(te => te.Prerequisites.Select(pr => pr.PrerequisiteVehicleTreeEntryId))
                    .ToList()
            })
            .ToListAsync();

        // Entry ids -> vehicle ids, client-side: keeps the server projection
        // SQLite-friendly (no APPLY) while the public contract still exposes
        // vehicle ids. Transitional until the version-aware API (Batch 7).
        var entryVehicleIds = await _context.VehicleTreeEntries
            .AsNoTracking()
            .Select(e => new { e.Id, e.VehicleId })
            .ToDictionaryAsync(e => e.Id, e => e.VehicleId);

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
                IsFolderParent = singleEntry?.IsFolderParent,
                FolderParentId = singleEntry?.FolderParentId,
                TreeColumn = singleEntry?.TreeColumn,
                TreeRow = singleEntry?.TreeRow,
                Prerequisites = v.PrerequisiteEntryIds
                    .Select(entryId => entryVehicleIds[entryId])
                    .Distinct()
                    .ToList(),
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
                IsFolderParent = e.FolderChildren.Any(),
                FolderParentId = e.FolderParentEntry == null
                    ? (Guid?)null
                    : e.FolderParentEntry.VehicleId,
                e.TreeColumn,
                e.TreeRow,
                PrerequisiteIds = e.Prerequisites
                    .Select(p => p.PrerequisiteVehicleTreeEntry.VehicleId)
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return Ok(entries);
    }
}
