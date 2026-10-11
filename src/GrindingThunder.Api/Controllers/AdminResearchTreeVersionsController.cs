using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrindingThunder.Api.Application.Exceptions;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Domain.Services;
using GrindingThunder.Api.Infrastructure.Persistence;

namespace GrindingThunder.Api.Controllers;

[ApiController]
[Route("api/admin/research-tree-versions")]
public class AdminResearchTreeVersionsController(
    IVersionLifecycleService lifecycleService,
    ApplicationDbContext context) : ControllerBase
{
    private readonly IVersionLifecycleService _lifecycleService = lifecycleService;
    private readonly ApplicationDbContext _context = context;

    /// <summary>
    /// Gets metadata for a specific research tree version.
    /// </summary>
    [HttpGet("{versionId:guid}")]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> GetVersion(
        [FromRoute] Guid versionId,
        CancellationToken cancellationToken)
    {
        var version = await _context.ResearchTreeVersions
            .AsNoTracking()
            .Include(v => v.GameUpdate)
            .Include(v => v.TreeRanks)
            .Include(v => v.Entries)
            .FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken);

        if (version is null)
        {
            return NotFound($"Research tree version '{versionId}' was not found.");
        }

        return Ok(new
        {
            version.Id,
            version.ResearchTreeId,
            version.GameUpdateId,
            GameUpdateVersion = version.GameUpdate.Version,
            Status = version.Status.ToString(),
            RankCount = version.TreeRanks.Count,
            EntryCount = version.Entries.Count
        });
    }

    /// <summary>
    /// Creates an initial blank draft research tree version for a research tree and game update.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> CreateDraftVersion(
        [FromBody] CreateDraftVersionRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        if (request.ResearchTreeId == Guid.Empty || request.GameUpdateId == Guid.Empty)
        {
            return BadRequest("ResearchTreeId and GameUpdateId are required.");
        }

        var researchTreeExists = await _context.ResearchTrees
            .AnyAsync(rt => rt.Id == request.ResearchTreeId, cancellationToken);

        if (!researchTreeExists)
        {
            return BadRequest($"Research tree '{request.ResearchTreeId}' was not found.");
        }

        var gameUpdateExists = await _context.GameUpdates
            .AnyAsync(gu => gu.Id == request.GameUpdateId, cancellationToken);

        if (!gameUpdateExists)
        {
            return BadRequest($"Game update '{request.GameUpdateId}' was not found.");
        }

        var versionExists = await _context.ResearchTreeVersions
            .AnyAsync(v => v.ResearchTreeId == request.ResearchTreeId && v.GameUpdateId == request.GameUpdateId, cancellationToken);

        if (versionExists)
        {
            return Conflict($"A research tree version already exists for research tree '{request.ResearchTreeId}' and game update '{request.GameUpdateId}'.");
        }

        var version = new ResearchTreeVersion
        {
            Id = Guid.NewGuid(),
            ResearchTreeId = request.ResearchTreeId,
            GameUpdateId = request.GameUpdateId,
            Status = ResearchTreeVersionStatus.Draft
        };

        _context.ResearchTreeVersions.Add(version);
        await _context.SaveChangesAsync(cancellationToken);

        var response = new ClonedVersionResponse(
            version.Id,
            version.ResearchTreeId,
            version.GameUpdateId,
            version.Status.ToString());

        return Created($"/api/admin/drafts/{version.Id}", response);
    }

    /// <summary>
    /// Clones an existing research tree version into an editable draft version for a target game update.
    /// </summary>
    [HttpPost("{versionId:guid}/clone")]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> CloneVersion(
        [FromRoute] Guid versionId,
        [FromBody] CloneVersionRequest request,
        CancellationToken cancellationToken)
    {
        if (versionId == Guid.Empty)
        {
            return BadRequest("versionId is required.");
        }

        if (request is null || request.TargetGameUpdateId == Guid.Empty)
        {
            return BadRequest("targetGameUpdateId is required.");
        }

        try
        {
            var cloned = await _lifecycleService.CloneVersionAsync(versionId, request.TargetGameUpdateId, cancellationToken);

            var response = new ClonedVersionResponse(
                cloned.Id,
                cloned.ResearchTreeId,
                cloned.GameUpdateId,
                cloned.Status.ToString());

            return Created($"/api/admin/drafts/{cloned.Id}", response);
        }
        catch (ResearchTreeVersionNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            if (exception.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(exception.Message);
            }

            return BadRequest(exception.Message);
        }
    }

    /// <summary>
    /// Validates and transitions a draft research tree version to Published status.
    /// Published versions become immutable snapshots.
    /// </summary>
    [HttpPost("{versionId:guid}/publish")]
    [Authorize(Policy = AuthPolicies.RequirePublisherRole)]
    public async Task<IActionResult> PublishVersion(
        [FromRoute] Guid versionId,
        CancellationToken cancellationToken)
    {
        if (versionId == Guid.Empty)
        {
            return BadRequest("versionId is required.");
        }

        try
        {
            await _lifecycleService.PublishVersionAsync(versionId, cancellationToken);
            return NoContent();
        }
        catch (ResearchTreeVersionNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            if (exception.Message.Contains("already published", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(exception.Message);
            }

            if (exception.Message.Contains("validation failed", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(exception.Message);
            }

            return Conflict(exception.Message);
        }
    }

    /// <summary>
    /// Deletes a draft research tree version and its associated versioned data.
    /// Published versions cannot be deleted.
    /// </summary>
    [HttpDelete("{versionId:guid}")]
    [Authorize(Policy = AuthPolicies.RequireAdminRole)]
    public async Task<IActionResult> DeleteVersion(
        [FromRoute] Guid versionId,
        CancellationToken cancellationToken)
    {
        if (versionId == Guid.Empty)
        {
            return BadRequest("versionId is required.");
        }

        try
        {
            await _lifecycleService.DeleteDraftVersionAsync(versionId, cancellationToken);
            return NoContent();
        }
        catch (ResearchTreeVersionNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(exception.Message);
        }
    }
}

