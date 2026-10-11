using Microsoft.AspNetCore.Mvc;
using GrindingThunder.Api.Application.Exceptions;
using GrindingThunder.Api.Domain.Services;

namespace GrindingThunder.Api.Controllers;

[ApiController]
[Route("api/research-tree-versions")]
public class ResearchTreeVersionsController(IVersionLifecycleService lifecycleService) : ControllerBase
{
    private readonly IVersionLifecycleService _lifecycleService = lifecycleService;

    /// <summary>
    /// Compares two research tree versions and returns detailed additions, removals, modifications,
    /// prerequisite changes, rank changes, and net cost deltas.
    /// Accessible via:
    /// - GET /api/research-tree-versions/diff?sourceVersionId={sourceId}&amp;targetVersionId={targetId}
    /// - GET /api/research-trees/{treeId}/diff?sourceVersionId={sourceId}&amp;targetVersionId={targetId}
    /// </summary>
    [HttpGet("diff")]
    [HttpGet("/api/research-trees/{treeId:guid}/diff")]
    public async Task<IActionResult> GetDiff(
        [FromQuery] Guid sourceVersionId,
        [FromQuery] Guid targetVersionId,
        CancellationToken cancellationToken)
    {
        if (sourceVersionId == Guid.Empty)
        {
            return BadRequest("sourceVersionId is required.");
        }

        if (targetVersionId == Guid.Empty)
        {
            return BadRequest("targetVersionId is required.");
        }

        try
        {
            var diff = await _lifecycleService.CompareVersionsAsync(sourceVersionId, targetVersionId, cancellationToken);
            return Ok(diff);
        }
        catch (ResearchTreeVersionNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    /// <summary>
    /// Validates the graph, rank rules, folder hierarchies, and cost/coordinate invariants
    /// for a specified research tree version.
    /// </summary>
    [HttpGet("{versionId:guid}/validate")]
    public async Task<IActionResult> ValidateVersion(
        [FromRoute] Guid versionId,
        CancellationToken cancellationToken)
    {
        if (versionId == Guid.Empty)
        {
            return BadRequest("versionId is required.");
        }

        try
        {
            var validation = await _lifecycleService.ValidateVersionAsync(versionId, cancellationToken);
            return Ok(validation);
        }
        catch (ResearchTreeVersionNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
    }
}

