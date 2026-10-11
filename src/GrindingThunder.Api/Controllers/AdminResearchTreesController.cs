using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;

namespace GrindingThunder.Api.Controllers;

[ApiController]
[Route("api/admin/research-trees")]
public class AdminResearchTreesController(ApplicationDbContext context) : ControllerBase
{
    private readonly ApplicationDbContext _context = context;

    [HttpGet]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var trees = await _context.ResearchTrees
            .AsNoTracking()
            .Include(rt => rt.Nation)
            .Include(rt => rt.VehicleType)
            .OrderBy(rt => rt.Nation.Name)
            .ThenBy(rt => rt.VehicleType.Name)
            .Select(rt => new ResearchTreeDto(
                rt.Id,
                rt.NationId,
                rt.Nation.Name,
                rt.VehicleTypeId,
                rt.VehicleType.Name))
            .ToListAsync(cancellationToken);

        return Ok(trees);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> GetById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var tree = await _context.ResearchTrees
            .AsNoTracking()
            .Include(rt => rt.Nation)
            .Include(rt => rt.VehicleType)
            .FirstOrDefaultAsync(rt => rt.Id == id, cancellationToken);

        if (tree is null)
        {
            return NotFound($"Research tree '{id}' was not found.");
        }

        return Ok(new ResearchTreeDto(
            tree.Id,
            tree.NationId,
            tree.Nation.Name,
            tree.VehicleTypeId,
            tree.VehicleType.Name));
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> Create(
        [FromBody] CreateResearchTreeRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        if (request.NationId == Guid.Empty || request.VehicleTypeId == Guid.Empty)
        {
            return BadRequest("NationId and VehicleTypeId are required.");
        }

        var nation = await _context.Nations
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == request.NationId, cancellationToken);

        if (nation is null)
        {
            return BadRequest($"Nation '{request.NationId}' was not found.");
        }

        var vehicleType = await _context.VehicleTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(vt => vt.Id == request.VehicleTypeId, cancellationToken);

        if (vehicleType is null)
        {
            return BadRequest($"Vehicle type '{request.VehicleTypeId}' was not found.");
        }

        var duplicate = await _context.ResearchTrees
            .AnyAsync(rt => rt.NationId == request.NationId && rt.VehicleTypeId == request.VehicleTypeId, cancellationToken);

        if (duplicate)
        {
            return Conflict($"A research tree for nation '{nation.Name}' and vehicle type '{vehicleType.Name}' already exists.");
        }

        var tree = new ResearchTree
        {
            Id = Guid.NewGuid(),
            NationId = request.NationId,
            VehicleTypeId = request.VehicleTypeId
        };

        _context.ResearchTrees.Add(tree);
        await _context.SaveChangesAsync(cancellationToken);

        var dto = new ResearchTreeDto(
            tree.Id,
            tree.NationId,
            nation.Name,
            tree.VehicleTypeId,
            vehicleType.Name);

        return CreatedAtAction(nameof(GetById), new { id = tree.Id }, dto);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireAdminRole)]
    public async Task<IActionResult> Delete(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var tree = await _context.ResearchTrees
            .Include(rt => rt.Versions)
            .FirstOrDefaultAsync(rt => rt.Id == id, cancellationToken);

        if (tree is null)
        {
            return NotFound($"Research tree '{id}' was not found.");
        }

        if (tree.Versions.Any())
        {
            return Conflict($"Cannot delete research tree '{id}' because it has associated versions.");
        }

        _context.ResearchTrees.Remove(tree);
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}

