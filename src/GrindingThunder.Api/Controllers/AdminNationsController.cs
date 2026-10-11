using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;

namespace GrindingThunder.Api.Controllers;

[ApiController]
[Route("api/admin/nations")]
public class AdminNationsController(ApplicationDbContext context) : ControllerBase
{
    private readonly ApplicationDbContext _context = context;

    [HttpGet]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var nations = await _context.Nations
            .AsNoTracking()
            .OrderBy(n => n.Name)
            .Select(n => new NationDto(n.Id, n.Name))
            .ToListAsync(cancellationToken);

        return Ok(nations);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> GetById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var nation = await _context.Nations
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

        if (nation is null)
        {
            return NotFound($"Nation '{id}' was not found.");
        }

        return Ok(new NationDto(nation.Id, nation.Name));
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> Create(
        [FromBody] CreateNationRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        var trimmedName = request.Name.Trim();

        var exists = await _context.Nations
            .AnyAsync(n => n.Name.ToLower() == trimmedName.ToLower(), cancellationToken);

        if (exists)
        {
            return Conflict($"Nation with name '{trimmedName}' already exists.");
        }

        var nation = new Nation
        {
            Id = Guid.NewGuid(),
            Name = trimmedName
        };

        _context.Nations.Add(nation);
        await _context.SaveChangesAsync(cancellationToken);

        var dto = new NationDto(nation.Id, nation.Name);
        return CreatedAtAction(nameof(GetById), new { id = nation.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateNationRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        var nation = await _context.Nations
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

        if (nation is null)
        {
            return NotFound($"Nation '{id}' was not found.");
        }

        var trimmedName = request.Name.Trim();

        var duplicate = await _context.Nations
            .AnyAsync(n => n.Id != id && n.Name.ToLower() == trimmedName.ToLower(), cancellationToken);

        if (duplicate)
        {
            return Conflict($"Nation with name '{trimmedName}' already exists.");
        }

        nation.Name = trimmedName;
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireAdminRole)]
    public async Task<IActionResult> Delete(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var nation = await _context.Nations
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

        if (nation is null)
        {
            return NotFound($"Nation '{id}' was not found.");
        }

        var hasTreeVersions = await _context.ResearchTrees
            .AnyAsync(rt => rt.NationId == id && rt.Versions.Any(), cancellationToken);

        if (hasTreeVersions)
        {
            return Conflict($"Cannot delete nation '{id}' because it has associated research tree versions.");
        }

        _context.Nations.Remove(nation);
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}

