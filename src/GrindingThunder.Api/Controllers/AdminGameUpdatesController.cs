using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;

namespace GrindingThunder.Api.Controllers;

[ApiController]
[Route("api/admin/game-updates")]
public class AdminGameUpdatesController(ApplicationDbContext context) : ControllerBase
{
    private readonly ApplicationDbContext _context = context;

    [HttpGet]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var updates = await _context.GameUpdates
            .AsNoTracking()
            .OrderByDescending(gu => gu.ReleaseDate)
            .ThenByDescending(gu => gu.Version)
            .Select(gu => new GameUpdateDto(gu.Id, gu.Version, gu.Name, gu.ReleaseDate))
            .ToListAsync(cancellationToken);

        return Ok(updates);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Editor,Publisher")]
    public async Task<IActionResult> GetById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var update = await _context.GameUpdates
            .AsNoTracking()
            .FirstOrDefaultAsync(gu => gu.Id == id, cancellationToken);

        if (update is null)
        {
            return NotFound($"Game update '{id}' was not found.");
        }

        return Ok(new GameUpdateDto(update.Id, update.Version, update.Name, update.ReleaseDate));
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> Create(
        [FromBody] CreateGameUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Version))
        {
            return BadRequest("Version is required.");
        }

        var trimmedVersion = request.Version.Trim();

        var exists = await _context.GameUpdates
            .AnyAsync(gu => gu.Version.ToLower() == trimmedVersion.ToLower(), cancellationToken);

        if (exists)
        {
            return Conflict($"Game update with version '{trimmedVersion}' already exists.");
        }

        var update = new GameUpdate
        {
            Id = Guid.NewGuid(),
            Version = trimmedVersion,
            Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(),
            ReleaseDate = request.ReleaseDate
        };

        _context.GameUpdates.Add(update);
        await _context.SaveChangesAsync(cancellationToken);

        var dto = new GameUpdateDto(update.Id, update.Version, update.Name, update.ReleaseDate);
        return CreatedAtAction(nameof(GetById), new { id = update.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateGameUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Version))
        {
            return BadRequest("Version is required.");
        }

        var update = await _context.GameUpdates
            .FirstOrDefaultAsync(gu => gu.Id == id, cancellationToken);

        if (update is null)
        {
            return NotFound($"Game update '{id}' was not found.");
        }

        var trimmedVersion = request.Version.Trim();

        var duplicate = await _context.GameUpdates
            .AnyAsync(gu => gu.Id != id && gu.Version.ToLower() == trimmedVersion.ToLower(), cancellationToken);

        if (duplicate)
        {
            return Conflict($"Game update with version '{trimmedVersion}' already exists.");
        }

        update.Version = trimmedVersion;
        update.Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        update.ReleaseDate = request.ReleaseDate;

        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireAdminRole)]
    public async Task<IActionResult> Delete(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var update = await _context.GameUpdates
            .FirstOrDefaultAsync(gu => gu.Id == id, cancellationToken);

        if (update is null)
        {
            return NotFound($"Game update '{id}' was not found.");
        }

        var hasVersions = await _context.ResearchTreeVersions
            .AnyAsync(rtv => rtv.GameUpdateId == id, cancellationToken);

        if (hasVersions)
        {
            return Conflict($"Cannot delete game update '{id}' because it is referenced by existing research tree versions.");
        }

        _context.GameUpdates.Remove(update);
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}

