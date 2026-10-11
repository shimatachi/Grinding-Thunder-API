using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;

namespace GrindingThunder.Api.Controllers;

[ApiController]
[Route("api/admin/drafts/{versionId:guid}")]
[Authorize(Policy = AuthPolicies.RequireEditorRole)]
public class AdminDraftsController(ApplicationDbContext context) : ControllerBase
{
    private readonly ApplicationDbContext _context = context;

    /// <summary>
    /// Guards all draft-editing endpoints: verifies existence and ensures the target version is in Draft status.
    /// Returns 404 Not Found if missing, or 409 Conflict if already published.
    /// </summary>
    private async Task<(ResearchTreeVersion? Version, IActionResult? ErrorResult)> EnsureDraftVersionAsync(
        Guid versionId, CancellationToken cancellationToken)
    {
        var version = await _context.ResearchTreeVersions
            .FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken);

        if (version is null)
        {
            return (null, NotFound($"Research tree version '{versionId}' was not found."));
        }

        if (version.Status != ResearchTreeVersionStatus.Draft)
        {
            return (null, Conflict($"Research tree version '{versionId}' is not in Draft status (status: {version.Status}). Published versions are immutable."));
        }

        return (version, null);
    }

    #region Draft Overview

    [HttpGet]
    public async Task<IActionResult> GetDraftOverview(
        [FromRoute] Guid versionId,
        CancellationToken cancellationToken)
    {
        var (version, error) = await EnsureDraftVersionAsync(versionId, cancellationToken);
        if (error is not null) return error;

        var ranks = await _context.TreeRanks
            .AsNoTracking()
            .Where(r => r.ResearchTreeVersionId == versionId)
            .OrderBy(r => r.RankNumber)
            .Select(r => new TreeRankDto(r.Id, r.ResearchTreeVersionId, r.RankNumber, r.RequiredVehiclesUnlocked))
            .ToListAsync(cancellationToken);

        var entries = await _context.VehicleTreeEntries
            .AsNoTracking()
            .Include(e => e.Vehicle)
            .Where(e => e.ResearchTreeVersionId == versionId)
            .OrderBy(e => e.TreeColumn)
            .ThenBy(e => e.TreeRow)
            .Select(e => new VehicleTreeEntryDto(
                e.Id,
                e.ResearchTreeVersionId,
                e.VehicleId,
                e.TreeRankId,
                e.RpCost,
                e.SlCost,
                e.TreeColumn,
                e.TreeRow,
                e.AcquisitionType,
                e.FolderParentEntryId,
                e.Vehicle.Name))
            .ToListAsync(cancellationToken);

        var prerequisites = await _context.VehiclePrerequisites
            .AsNoTracking()
            .Where(p => p.ResearchTreeVersionId == versionId)
            .Select(p => new VehiclePrerequisiteDto(
                p.VehicleTreeEntryId,
                p.PrerequisiteVehicleTreeEntryId,
                p.ResearchTreeVersionId))
            .ToListAsync(cancellationToken);

        var overview = new DraftVersionDetailDto(
            version!.Id,
            version.ResearchTreeId,
            version.GameUpdateId,
            version.Status.ToString(),
            ranks,
            entries,
            prerequisites);

        return Ok(overview);
    }

    #endregion

    #region TreeRank Management

    [HttpGet("ranks")]
    public async Task<IActionResult> GetRanks(
        [FromRoute] Guid versionId,
        CancellationToken cancellationToken)
    {
        var (_, error) = await EnsureDraftVersionAsync(versionId, cancellationToken);
        if (error is not null) return error;

        var ranks = await _context.TreeRanks
            .AsNoTracking()
            .Where(r => r.ResearchTreeVersionId == versionId)
            .OrderBy(r => r.RankNumber)
            .Select(r => new TreeRankDto(r.Id, r.ResearchTreeVersionId, r.RankNumber, r.RequiredVehiclesUnlocked))
            .ToListAsync(cancellationToken);

        return Ok(ranks);
    }

    [HttpGet("ranks/{rankId:guid}")]
    public async Task<IActionResult> GetRankById(
        [FromRoute] Guid versionId,
        [FromRoute] Guid rankId,
        CancellationToken cancellationToken)
    {
        var (_, error) = await EnsureDraftVersionAsync(versionId, cancellationToken);
        if (error is not null) return error;

        var rank = await _context.TreeRanks
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == rankId && r.ResearchTreeVersionId == versionId, cancellationToken);

        if (rank is null)
        {
            return NotFound($"Tree rank '{rankId}' was not found in version '{versionId}'.");
        }

        return Ok(new TreeRankDto(rank.Id, rank.ResearchTreeVersionId, rank.RankNumber, rank.RequiredVehiclesUnlocked));
    }

    [HttpPost("ranks")]
    public async Task<IActionResult> AddRank(
        [FromRoute] Guid versionId,
        [FromBody] AddTreeRankRequest request,
        CancellationToken cancellationToken)
    {
        var (_, error) = await EnsureDraftVersionAsync(versionId, cancellationToken);
        if (error is not null) return error;

        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        if (request.RankNumber <= 0)
        {
            return BadRequest("Rank number must be positive.");
        }

        if (request.RequiredVehiclesUnlocked < 0)
        {
            return BadRequest("Required vehicles unlocked cannot be negative.");
        }

        var rankExists = await _context.TreeRanks
            .AnyAsync(r => r.ResearchTreeVersionId == versionId && r.RankNumber == request.RankNumber, cancellationToken);

        if (rankExists)
        {
            return Conflict($"Rank {request.RankNumber} already exists in draft version '{versionId}'.");
        }

        var rank = new TreeRank
        {
            Id = Guid.NewGuid(),
            ResearchTreeVersionId = versionId,
            RankNumber = request.RankNumber,
            RequiredVehiclesUnlocked = request.RequiredVehiclesUnlocked
        };

        _context.TreeRanks.Add(rank);
        await _context.SaveChangesAsync(cancellationToken);

        var dto = new TreeRankDto(rank.Id, rank.ResearchTreeVersionId, rank.RankNumber, rank.RequiredVehiclesUnlocked);
        return CreatedAtAction(nameof(GetRankById), new { versionId, rankId = rank.Id }, dto);
    }

    [HttpPut("ranks/{rankId:guid}")]
    public async Task<IActionResult> UpdateRank(
        [FromRoute] Guid versionId,
        [FromRoute] Guid rankId,
        [FromBody] UpdateTreeRankQuotaRequest request,
        CancellationToken cancellationToken)
    {
        var (_, error) = await EnsureDraftVersionAsync(versionId, cancellationToken);
        if (error is not null) return error;

        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        if (request.RequiredVehiclesUnlocked < 0)
        {
            return BadRequest("Required vehicles unlocked cannot be negative.");
        }

        var rank = await _context.TreeRanks
            .FirstOrDefaultAsync(r => r.Id == rankId && r.ResearchTreeVersionId == versionId, cancellationToken);

        if (rank is null)
        {
            return NotFound($"Tree rank '{rankId}' was not found in version '{versionId}'.");
        }

        rank.RequiredVehiclesUnlocked = request.RequiredVehiclesUnlocked;
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpDelete("ranks/{rankId:guid}")]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> DeleteRank(
        [FromRoute] Guid versionId,
        [FromRoute] Guid rankId,
        CancellationToken cancellationToken)
    {
        var (_, error) = await EnsureDraftVersionAsync(versionId, cancellationToken);
        if (error is not null) return error;

        var rank = await _context.TreeRanks
            .FirstOrDefaultAsync(r => r.Id == rankId && r.ResearchTreeVersionId == versionId, cancellationToken);

        if (rank is null)
        {
            return NotFound($"Tree rank '{rankId}' was not found in version '{versionId}'.");
        }

        var hasEntries = await _context.VehicleTreeEntries
            .AnyAsync(e => e.TreeRankId == rankId && e.ResearchTreeVersionId == versionId, cancellationToken);

        if (hasEntries)
        {
            return Conflict($"Cannot delete tree rank '{rankId}' because it contains vehicle tree entries.");
        }

        _context.TreeRanks.Remove(rank);
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    #endregion

    #region VehicleTreeEntry Management

    [HttpGet("entries")]
    public async Task<IActionResult> GetEntries(
        [FromRoute] Guid versionId,
        CancellationToken cancellationToken)
    {
        var (_, error) = await EnsureDraftVersionAsync(versionId, cancellationToken);
        if (error is not null) return error;

        var entries = await _context.VehicleTreeEntries
            .AsNoTracking()
            .Include(e => e.Vehicle)
            .Where(e => e.ResearchTreeVersionId == versionId)
            .OrderBy(e => e.TreeColumn)
            .ThenBy(e => e.TreeRow)
            .Select(e => new VehicleTreeEntryDto(
                e.Id,
                e.ResearchTreeVersionId,
                e.VehicleId,
                e.TreeRankId,
                e.RpCost,
                e.SlCost,
                e.TreeColumn,
                e.TreeRow,
                e.AcquisitionType,
                e.FolderParentEntryId,
                e.Vehicle.Name))
            .ToListAsync(cancellationToken);

        return Ok(entries);
    }

    [HttpGet("entries/{entryId:guid}")]
    public async Task<IActionResult> GetEntryById(
        [FromRoute] Guid versionId,
        [FromRoute] Guid entryId,
        CancellationToken cancellationToken)
    {
        var (_, error) = await EnsureDraftVersionAsync(versionId, cancellationToken);
        if (error is not null) return error;

        var entry = await _context.VehicleTreeEntries
            .AsNoTracking()
            .Include(e => e.Vehicle)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.ResearchTreeVersionId == versionId, cancellationToken);

        if (entry is null)
        {
            return NotFound($"Vehicle tree entry '{entryId}' was not found in version '{versionId}'.");
        }

        return Ok(new VehicleTreeEntryDto(
            entry.Id,
            entry.ResearchTreeVersionId,
            entry.VehicleId,
            entry.TreeRankId,
            entry.RpCost,
            entry.SlCost,
            entry.TreeColumn,
            entry.TreeRow,
            entry.AcquisitionType,
            entry.FolderParentEntryId,
            entry.Vehicle.Name));
    }

    [HttpPost("entries")]
    public async Task<IActionResult> AddEntry(
        [FromRoute] Guid versionId,
        [FromBody] AddVehicleTreeEntryRequest request,
        CancellationToken cancellationToken)
    {
        var (_, error) = await EnsureDraftVersionAsync(versionId, cancellationToken);
        if (error is not null) return error;

        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        if (request.VehicleId == Guid.Empty)
        {
            return BadRequest("VehicleId is required.");
        }

        if (request.TreeRankId == Guid.Empty)
        {
            return BadRequest("TreeRankId is required.");
        }

        if (request.RpCost < 0 || request.SlCost < 0)
        {
            return BadRequest("RP and SL costs cannot be negative.");
        }

        if (request.TreeColumn < 0 || request.TreeRow < 0)
        {
            return BadRequest("TreeColumn and TreeRow must be non-negative.");
        }

        if (!Enum.IsDefined(request.AcquisitionType))
        {
            return BadRequest("Invalid AcquisitionType.");
        }

        var vehicle = await _context.Vehicles
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);

        if (vehicle is null)
        {
            return NotFound($"Vehicle '{request.VehicleId}' was not found.");
        }

        var rankExists = await _context.TreeRanks
            .AnyAsync(r => r.Id == request.TreeRankId && r.ResearchTreeVersionId == versionId, cancellationToken);

        if (!rankExists)
        {
            return BadRequest($"Tree rank '{request.TreeRankId}' does not belong to version '{versionId}'.");
        }

        var vehicleEntryExists = await _context.VehicleTreeEntries
            .AnyAsync(e => e.ResearchTreeVersionId == versionId && e.VehicleId == request.VehicleId, cancellationToken);

        if (vehicleEntryExists)
        {
            return Conflict($"Vehicle '{request.VehicleId}' already has an entry in draft version '{versionId}'.");
        }

        if (request.FolderParentEntryId.HasValue)
        {
            var parentExists = await _context.VehicleTreeEntries
                .AnyAsync(e => e.Id == request.FolderParentEntryId.Value && e.ResearchTreeVersionId == versionId, cancellationToken);

            if (!parentExists)
            {
                return BadRequest($"Folder parent entry '{request.FolderParentEntryId.Value}' does not belong to version '{versionId}'.");
            }
        }

        var entry = new VehicleTreeEntry
        {
            Id = Guid.NewGuid(),
            ResearchTreeVersionId = versionId,
            VehicleId = request.VehicleId,
            TreeRankId = request.TreeRankId,
            RpCost = request.RpCost,
            SlCost = request.SlCost,
            TreeColumn = request.TreeColumn,
            TreeRow = request.TreeRow,
            AcquisitionType = request.AcquisitionType,
            FolderParentEntryId = request.FolderParentEntryId
        };

        _context.VehicleTreeEntries.Add(entry);
        await _context.SaveChangesAsync(cancellationToken);

        var dto = new VehicleTreeEntryDto(
            entry.Id,
            entry.ResearchTreeVersionId,
            entry.VehicleId,
            entry.TreeRankId,
            entry.RpCost,
            entry.SlCost,
            entry.TreeColumn,
            entry.TreeRow,
            entry.AcquisitionType,
            entry.FolderParentEntryId,
            vehicle.Name);

        return CreatedAtAction(nameof(GetEntryById), new { versionId, entryId = entry.Id }, dto);
    }

    [HttpPut("entries/{entryId:guid}")]
    public async Task<IActionResult> UpdateEntry(
        [FromRoute] Guid versionId,
        [FromRoute] Guid entryId,
        [FromBody] UpdateVehicleTreeEntryRequest request,
        CancellationToken cancellationToken)
    {
        var (_, error) = await EnsureDraftVersionAsync(versionId, cancellationToken);
        if (error is not null) return error;

        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        if (request.RpCost < 0 || request.SlCost < 0)
        {
            return BadRequest("RP and SL costs cannot be negative.");
        }

        if (request.TreeColumn < 0 || request.TreeRow < 0)
        {
            return BadRequest("TreeColumn and TreeRow must be non-negative.");
        }

        if (!Enum.IsDefined(request.AcquisitionType))
        {
            return BadRequest("Invalid AcquisitionType.");
        }

        var entry = await _context.VehicleTreeEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.ResearchTreeVersionId == versionId, cancellationToken);

        if (entry is null)
        {
            return NotFound($"Vehicle tree entry '{entryId}' was not found in version '{versionId}'.");
        }

        var rankExists = await _context.TreeRanks
            .AnyAsync(r => r.Id == request.TreeRankId && r.ResearchTreeVersionId == versionId, cancellationToken);

        if (!rankExists)
        {
            return BadRequest($"Tree rank '{request.TreeRankId}' does not belong to version '{versionId}'.");
        }

        if (request.FolderParentEntryId.HasValue)
        {
            if (request.FolderParentEntryId.Value == entryId)
            {
                return BadRequest("An entry cannot be its own folder parent.");
            }

            var parentExists = await _context.VehicleTreeEntries
                .AnyAsync(e => e.Id == request.FolderParentEntryId.Value && e.ResearchTreeVersionId == versionId, cancellationToken);

            if (!parentExists)
            {
                return BadRequest($"Folder parent entry '{request.FolderParentEntryId.Value}' does not belong to version '{versionId}'.");
            }
        }

        entry.RpCost = request.RpCost;
        entry.SlCost = request.SlCost;
        entry.TreeColumn = request.TreeColumn;
        entry.TreeRow = request.TreeRow;
        entry.TreeRankId = request.TreeRankId;
        entry.AcquisitionType = request.AcquisitionType;
        entry.FolderParentEntryId = request.FolderParentEntryId;

        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPut("entries/{entryId:guid}/costs")]
    [HttpPatch("entries/{entryId:guid}/costs")]
    public async Task<IActionResult> UpdateEntryCosts(
        [FromRoute] Guid versionId,
        [FromRoute] Guid entryId,
        [FromBody] UpdateEntryCostsRequest request,
        CancellationToken cancellationToken)
    {
        var (_, error) = await EnsureDraftVersionAsync(versionId, cancellationToken);
        if (error is not null) return error;

        if (request is null || request.RpCost < 0 || request.SlCost < 0)
        {
            return BadRequest("RP and SL costs cannot be negative.");
        }

        var entry = await _context.VehicleTreeEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.ResearchTreeVersionId == versionId, cancellationToken);

        if (entry is null)
        {
            return NotFound($"Vehicle tree entry '{entryId}' was not found in version '{versionId}'.");
        }

        entry.RpCost = request.RpCost;
        entry.SlCost = request.SlCost;

        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPut("entries/{entryId:guid}/coordinates")]
    [HttpPatch("entries/{entryId:guid}/coordinates")]
    public async Task<IActionResult> UpdateEntryCoordinates(
        [FromRoute] Guid versionId,
        [FromRoute] Guid entryId,
        [FromBody] UpdateEntryCoordinatesRequest request,
        CancellationToken cancellationToken)
    {
        var (_, error) = await EnsureDraftVersionAsync(versionId, cancellationToken);
        if (error is not null) return error;

        if (request is null || request.TreeColumn < 0 || request.TreeRow < 0)
        {
            return BadRequest("TreeColumn and TreeRow must be non-negative.");
        }

        var entry = await _context.VehicleTreeEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.ResearchTreeVersionId == versionId, cancellationToken);

        if (entry is null)
        {
            return NotFound($"Vehicle tree entry '{entryId}' was not found in version '{versionId}'.");
        }

        entry.TreeColumn = request.TreeColumn;
        entry.TreeRow = request.TreeRow;

        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPut("entries/{entryId:guid}/folder-parent")]
    [HttpPatch("entries/{entryId:guid}/folder-parent")]
    public async Task<IActionResult> UpdateEntryFolderParent(
        [FromRoute] Guid versionId,
        [FromRoute] Guid entryId,
        [FromBody] UpdateEntryFolderParentRequest request,
        CancellationToken cancellationToken)
    {
        var (_, error) = await EnsureDraftVersionAsync(versionId, cancellationToken);
        if (error is not null) return error;

        var entry = await _context.VehicleTreeEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.ResearchTreeVersionId == versionId, cancellationToken);

        if (entry is null)
        {
            return NotFound($"Vehicle tree entry '{entryId}' was not found in version '{versionId}'.");
        }

        if (request?.FolderParentEntryId is not null)
        {
            if (request.FolderParentEntryId.Value == entryId)
            {
                return BadRequest("An entry cannot be its own folder parent.");
            }

            var parentExists = await _context.VehicleTreeEntries
                .AnyAsync(e => e.Id == request.FolderParentEntryId.Value && e.ResearchTreeVersionId == versionId, cancellationToken);

            if (!parentExists)
            {
                return BadRequest($"Folder parent entry '{request.FolderParentEntryId.Value}' does not belong to version '{versionId}'.");
            }

            entry.FolderParentEntryId = request.FolderParentEntryId.Value;
        }
        else
        {
            entry.FolderParentEntryId = null;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpDelete("entries/{entryId:guid}")]
    public async Task<IActionResult> DeleteEntry(
        [FromRoute] Guid versionId,
        [FromRoute] Guid entryId,
        CancellationToken cancellationToken)
    {
        var (_, error) = await EnsureDraftVersionAsync(versionId, cancellationToken);
        if (error is not null) return error;

        var entry = await _context.VehicleTreeEntries
            .Include(e => e.FolderChildren)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.ResearchTreeVersionId == versionId, cancellationToken);

        if (entry is null)
        {
            return NotFound($"Vehicle tree entry '{entryId}' was not found in version '{versionId}'.");
        }

        if (entry.FolderChildren.Any())
        {
            return Conflict($"Cannot delete vehicle tree entry '{entryId}' because it has nested child folder entries.");
        }

        _context.VehicleTreeEntries.Remove(entry);
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    #endregion

    #region VehiclePrerequisite Management

    [HttpGet("prerequisites")]
    public async Task<IActionResult> GetPrerequisites(
        [FromRoute] Guid versionId,
        CancellationToken cancellationToken)
    {
        var (_, error) = await EnsureDraftVersionAsync(versionId, cancellationToken);
        if (error is not null) return error;

        var prerequisites = await _context.VehiclePrerequisites
            .AsNoTracking()
            .Where(p => p.ResearchTreeVersionId == versionId)
            .Select(p => new VehiclePrerequisiteDto(
                p.VehicleTreeEntryId,
                p.PrerequisiteVehicleTreeEntryId,
                p.ResearchTreeVersionId))
            .ToListAsync(cancellationToken);

        return Ok(prerequisites);
    }

    [HttpPost("prerequisites")]
    public async Task<IActionResult> AddPrerequisite(
        [FromRoute] Guid versionId,
        [FromBody] AddPrerequisiteRequest request,
        CancellationToken cancellationToken)
    {
        var (_, error) = await EnsureDraftVersionAsync(versionId, cancellationToken);
        if (error is not null) return error;

        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        if (request.VehicleTreeEntryId == Guid.Empty || request.PrerequisiteVehicleTreeEntryId == Guid.Empty)
        {
            return BadRequest("VehicleTreeEntryId and PrerequisiteVehicleTreeEntryId are required.");
        }

        if (request.VehicleTreeEntryId == request.PrerequisiteVehicleTreeEntryId)
        {
            return BadRequest("An entry cannot be a prerequisite of itself.");
        }

        var entryExists = await _context.VehicleTreeEntries
            .AnyAsync(e => e.Id == request.VehicleTreeEntryId && e.ResearchTreeVersionId == versionId, cancellationToken);

        var prereqExists = await _context.VehicleTreeEntries
            .AnyAsync(e => e.Id == request.PrerequisiteVehicleTreeEntryId && e.ResearchTreeVersionId == versionId, cancellationToken);

        if (!entryExists || !prereqExists)
        {
            return NotFound("One or both vehicle tree entries were not found in this draft version.");
        }

        var edgeExists = await _context.VehiclePrerequisites
            .AnyAsync(p => p.ResearchTreeVersionId == versionId &&
                           p.VehicleTreeEntryId == request.VehicleTreeEntryId &&
                           p.PrerequisiteVehicleTreeEntryId == request.PrerequisiteVehicleTreeEntryId, cancellationToken);

        if (edgeExists)
        {
            return Conflict("Prerequisite edge already exists in this draft version.");
        }

        var prerequisite = new VehiclePrerequisite
        {
            ResearchTreeVersionId = versionId,
            VehicleTreeEntryId = request.VehicleTreeEntryId,
            PrerequisiteVehicleTreeEntryId = request.PrerequisiteVehicleTreeEntryId
        };

        _context.VehiclePrerequisites.Add(prerequisite);
        await _context.SaveChangesAsync(cancellationToken);

        var dto = new VehiclePrerequisiteDto(
            prerequisite.VehicleTreeEntryId,
            prerequisite.PrerequisiteVehicleTreeEntryId,
            prerequisite.ResearchTreeVersionId);

        return Created($"/api/admin/drafts/{versionId}/prerequisites", dto);
    }

    [HttpDelete("prerequisites/{vehicleTreeEntryId:guid}/{prerequisiteVehicleTreeEntryId:guid}")]
    [HttpDelete("prerequisites")]
    public async Task<IActionResult> RemovePrerequisite(
        [FromRoute] Guid versionId,
        [FromRoute] Guid? vehicleTreeEntryId,
        [FromRoute] Guid? prerequisiteVehicleTreeEntryId,
        [FromQuery] Guid? entryId,
        [FromQuery] Guid? prereqId,
        CancellationToken cancellationToken)
    {
        var (_, error) = await EnsureDraftVersionAsync(versionId, cancellationToken);
        if (error is not null) return error;

        var targetEntryId = vehicleTreeEntryId ?? entryId;
        var targetPrereqId = prerequisiteVehicleTreeEntryId ?? prereqId;

        if (targetEntryId is null || targetPrereqId is null || targetEntryId == Guid.Empty || targetPrereqId == Guid.Empty)
        {
            return BadRequest("VehicleTreeEntryId and PrerequisiteVehicleTreeEntryId must be specified.");
        }

        var prerequisite = await _context.VehiclePrerequisites
            .FirstOrDefaultAsync(p => p.ResearchTreeVersionId == versionId &&
                                      p.VehicleTreeEntryId == targetEntryId.Value &&
                                      p.PrerequisiteVehicleTreeEntryId == targetPrereqId.Value, cancellationToken);

        if (prerequisite is null)
        {
            return NotFound("Prerequisite edge was not found in this draft version.");
        }

        _context.VehiclePrerequisites.Remove(prerequisite);
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    #endregion
}

