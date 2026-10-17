using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrindingThunder.Api.Domain.Entities;
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
                v.ResearchTreeId,
                v.ResearchTree.NationId,
                NationName = v.ResearchTree.Nation.Name,
                v.ResearchTree.VehicleTypeId,
                VehicleTypeName = v.ResearchTree.VehicleType.Name,
                TreeEntries = v.TreeEntries.Select(e => new
                {
                    e.ResearchTreeVersionId,
                    e.TreeRankId,
                    RankNumber = (int?)e.TreeRank.RankNumber,
                    e.RpCost,
                    e.SlCost,
                    e.AcquisitionType,
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
                v.WikiUrl,
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
                v.ResearchTreeId,
                v.NationId,
                v.NationName,
                v.VehicleTypeId,
                v.VehicleTypeName,
                RankId = singleEntry?.TreeRankId,
                v.Name,
                v.ImageUrl,
                v.WikiUrl,
                RpCost = singleEntry?.RpCost,
                SlCost = singleEntry?.SlCost,
                AcquisitionType = singleEntry?.AcquisitionType,
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
        [FromQuery] Guid researchTreeVersionId,
        [FromQuery] string? type,
        CancellationToken cancellationToken)
    {
        if (nationId == Guid.Empty)
        {
            return BadRequest("nationId is required.");
        }

        if (researchTreeVersionId == Guid.Empty)
        {
            return BadRequest("researchTreeVersionId is required.");
        }

        var normalizedType = string.IsNullOrWhiteSpace(type) ? null : type.Trim().ToLowerInvariant();

        // Treat Draft snapshots as absent from the public resource space. This
        // check happens before entry projection so an empty Draft cannot leak its
        // existence through a distinguishable successful response.
        var publishedVersionExists = await _context.ResearchTreeVersions
            .AsNoTracking()
            .AnyAsync(version =>
                version.Id == researchTreeVersionId &&
                version.Status == ResearchTreeVersionStatus.Published &&
                version.ResearchTree.NationId == nationId &&
                (normalizedType == null ||
                 version.ResearchTree.VehicleType.Name.ToLower() == normalizedType),
                cancellationToken);

        if (!publishedVersionExists)
        {
            return NotFound();
        }

        // All snapshot-specific values come from entries owned by the exact
        // Published version selected above.
        var query = _context.VehicleTreeEntries
            .AsNoTracking()
            .Where(e =>
                e.ResearchTreeVersionId == researchTreeVersionId &&
                e.TreeRank.ResearchTreeVersion.Status == ResearchTreeVersionStatus.Published &&
                e.TreeRank.ResearchTreeVersion.ResearchTree.NationId == nationId);

        if (normalizedType is not null)
        {
            query = query.Where(e => e.TreeRank.ResearchTreeVersion.ResearchTree.VehicleType.Name.ToLower() == normalizedType);
        }

        var entries = await query
            .Select(e => new
            {
                e.Vehicle.Id,
                e.Vehicle.ResearchTreeId,
                e.Vehicle.ResearchTree.NationId,
                NationName = e.Vehicle.ResearchTree.Nation.Name,
                e.Vehicle.ResearchTree.VehicleTypeId,
                VehicleTypeName = e.Vehicle.ResearchTree.VehicleType.Name,
                e.TreeRankId,
                e.TreeRank.ResearchTreeVersionId,
                e.Vehicle.Name,
                e.Vehicle.ImageUrl,
                e.Vehicle.WikiUrl,
                e.RpCost,
                e.SlCost,
                e.AcquisitionType,
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
