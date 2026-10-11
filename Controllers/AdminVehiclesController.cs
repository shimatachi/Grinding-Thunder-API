using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;

namespace GrindingThunder.Api.Controllers;

[ApiController]
[Route("api/admin/vehicles")]
public class AdminVehiclesController(ApplicationDbContext context) : ControllerBase
{
    private readonly ApplicationDbContext _context = context;

    [HttpGet]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var vehicles = await _context.Vehicles
            .AsNoTracking()
            .OrderBy(v => v.Name)
            .Select(v => new VehicleDto(v.Id, v.Name, v.ImageUrl))
            .ToListAsync(cancellationToken);

        return Ok(vehicles);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> GetById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var vehicle = await _context.Vehicles
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        if (vehicle is null)
        {
            return NotFound($"Vehicle '{id}' was not found.");
        }

        return Ok(new VehicleDto(vehicle.Id, vehicle.Name, vehicle.ImageUrl));
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> Create(
        [FromBody] CreateVehicleRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        var trimmedName = request.Name.Trim();

        var exists = await _context.Vehicles
            .AnyAsync(v => v.Name.ToLower() == trimmedName.ToLower(), cancellationToken);

        if (exists)
        {
            return Conflict($"Vehicle with name '{trimmedName}' already exists.");
        }

        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim()
        };

        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync(cancellationToken);

        var dto = new VehicleDto(vehicle.Id, vehicle.Name, vehicle.ImageUrl);
        return CreatedAtAction(nameof(GetById), new { id = vehicle.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateVehicleRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        if (vehicle is null)
        {
            return NotFound($"Vehicle '{id}' was not found.");
        }

        var trimmedName = request.Name.Trim();

        var duplicate = await _context.Vehicles
            .AnyAsync(v => v.Id != id && v.Name.ToLower() == trimmedName.ToLower(), cancellationToken);

        if (duplicate)
        {
            return Conflict($"Vehicle with name '{trimmedName}' already exists.");
        }

        vehicle.Name = trimmedName;
        vehicle.ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim();

        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireAdminRole)]
    public async Task<IActionResult> Delete(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        if (vehicle is null)
        {
            return NotFound($"Vehicle '{id}' was not found.");
        }

        var hasTreeEntries = await _context.VehicleTreeEntries
            .AnyAsync(e => e.VehicleId == id, cancellationToken);

        if (hasTreeEntries)
        {
            return Conflict($"Cannot delete vehicle '{id}' because it is referenced by existing research tree entries.");
        }

        _context.Vehicles.Remove(vehicle);
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}

