using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;

namespace GrindingThunder.Api.Controllers;

[ApiController]
[Route("api/admin/vehicle-types")]
public class AdminVehicleTypesController(ApplicationDbContext context) : ControllerBase
{
    private readonly ApplicationDbContext _context = context;

    [HttpGet]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var types = await _context.VehicleTypes
            .AsNoTracking()
            .OrderBy(vt => vt.Name)
            .Select(vt => new VehicleTypeDto(vt.Id, vt.Name))
            .ToListAsync(cancellationToken);

        return Ok(types);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> GetById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var type = await _context.VehicleTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(vt => vt.Id == id, cancellationToken);

        if (type is null)
        {
            return NotFound($"Vehicle type '{id}' was not found.");
        }

        return Ok(new VehicleTypeDto(type.Id, type.Name));
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> Create(
        [FromBody] CreateVehicleTypeRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        var trimmedName = request.Name.Trim();

        var exists = await _context.VehicleTypes
            .AnyAsync(vt => vt.Name.ToLower() == trimmedName.ToLower(), cancellationToken);

        if (exists)
        {
            return Conflict($"Vehicle type with name '{trimmedName}' already exists.");
        }

        var vehicleType = new VehicleType
        {
            Id = Guid.NewGuid(),
            Name = trimmedName
        };

        _context.VehicleTypes.Add(vehicleType);
        await _context.SaveChangesAsync(cancellationToken);

        var dto = new VehicleTypeDto(vehicleType.Id, vehicleType.Name);
        return CreatedAtAction(nameof(GetById), new { id = vehicleType.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateVehicleTypeRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        var vehicleType = await _context.VehicleTypes
            .FirstOrDefaultAsync(vt => vt.Id == id, cancellationToken);

        if (vehicleType is null)
        {
            return NotFound($"Vehicle type '{id}' was not found.");
        }

        var trimmedName = request.Name.Trim();

        var duplicate = await _context.VehicleTypes
            .AnyAsync(vt => vt.Id != id && vt.Name.ToLower() == trimmedName.ToLower(), cancellationToken);

        if (duplicate)
        {
            return Conflict($"Vehicle type with name '{trimmedName}' already exists.");
        }

        vehicleType.Name = trimmedName;
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireAdminRole)]
    public async Task<IActionResult> Delete(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var vehicleType = await _context.VehicleTypes
            .FirstOrDefaultAsync(vt => vt.Id == id, cancellationToken);

        if (vehicleType is null)
        {
            return NotFound($"Vehicle type '{id}' was not found.");
        }

        var hasTreeVersions = await _context.ResearchTrees
            .AnyAsync(rt => rt.VehicleTypeId == id && rt.Versions.Any(), cancellationToken);

        if (hasTreeVersions)
        {
            return Conflict($"Cannot delete vehicle type '{id}' because it has associated research tree versions.");
        }

        _context.VehicleTypes.Remove(vehicleType);
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}

