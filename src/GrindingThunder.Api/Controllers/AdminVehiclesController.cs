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

    // Stable identities include vehicles which have not yet been added to a version.
    // Publishers can browse this bounded projection without access to catalog mutations.
    [HttpGet("tree/{researchTreeId:guid}")]
    [Authorize(Roles = "Admin,Editor,Publisher")]
    public async Task<IActionResult> GetByTree(Guid researchTreeId, CancellationToken cancellationToken)
    {
        if (!await _context.ResearchTrees.AnyAsync(t => t.Id == researchTreeId, cancellationToken))
            return NotFound($"Research tree '{researchTreeId}' was not found.");

        var vehicles = await _context.Vehicles.AsNoTracking()
            .Where(v => v.ResearchTreeId == researchTreeId)
            .OrderBy(v => v.Name)
            .Select(v => new VehicleDto(v.Id, v.Name, v.ImageUrl, v.WikiUrl, v.ResearchTreeId,
                v.ResearchTree.NationId, v.ResearchTree.Nation.Name,
                v.ResearchTree.VehicleTypeId, v.ResearchTree.VehicleType.Name))
            .ToListAsync(cancellationToken);
        return Ok(vehicles);
    }

    [HttpGet]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var vehicles = await _context.Vehicles
            .AsNoTracking()
            .OrderBy(v => v.Name)
            .Select(v => new VehicleDto(v.Id, v.Name, v.ImageUrl, v.WikiUrl, v.ResearchTreeId,
                v.ResearchTree.NationId, v.ResearchTree.Nation.Name,
                v.ResearchTree.VehicleTypeId, v.ResearchTree.VehicleType.Name))
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
            .Include(v => v.ResearchTree).ThenInclude(t => t.Nation)
            .Include(v => v.ResearchTree).ThenInclude(t => t.VehicleType)
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        if (vehicle is null)
        {
            return NotFound($"Vehicle '{id}' was not found.");
        }

        return Ok(ToDto(vehicle));
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

        if (trimmedName.Length > 150) return BadRequest("Name must be at most 150 characters.");
        if (request.WikiUrl?.Length > 2048) return BadRequest("WikiUrl must be at most 2048 characters.");
        if (request.ResearchTreeId == Guid.Empty) return BadRequest("ResearchTreeId is required.");
        var tree = await _context.ResearchTrees
            .Include(t => t.Nation).Include(t => t.VehicleType)
            .SingleOrDefaultAsync(t => t.Id == request.ResearchTreeId, cancellationToken);
        if (tree is null) return BadRequest($"Research tree '{request.ResearchTreeId}' was not found.");

        var exists = await _context.Vehicles
            .AnyAsync(v => v.ResearchTreeId == request.ResearchTreeId &&
                v.Name.Trim().ToLower() == trimmedName.ToLower(), cancellationToken);

        if (exists)
        {
            return Conflict($"Vehicle with name '{trimmedName}' already exists.");
        }

        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            ResearchTreeId = tree.Id,
            ResearchTree = tree,
            Name = trimmedName,
            ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim(),
            WikiUrl = string.IsNullOrWhiteSpace(request.WikiUrl) ? null : request.WikiUrl.Trim()
        };

        _context.Vehicles.Add(vehicle);
        try { await _context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (IsDuplicateName(ex))
        {
            return Conflict($"Vehicle with name '{trimmedName}' already exists in this research tree.");
        }

        var dto = ToDto(vehicle);
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
        if (trimmedName.Length > 150) return BadRequest("Name must be at most 150 characters.");
        if (request.WikiUrl?.Length > 2048) return BadRequest("WikiUrl must be at most 2048 characters.");

        var duplicate = await _context.Vehicles
            .AnyAsync(v => v.Id != id && v.ResearchTreeId == vehicle.ResearchTreeId &&
                v.Name.Trim().ToLower() == trimmedName.ToLower(), cancellationToken);

        if (duplicate)
        {
            return Conflict($"Vehicle with name '{trimmedName}' already exists.");
        }

        vehicle.Name = trimmedName;
        vehicle.ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim();
        vehicle.WikiUrl = string.IsNullOrWhiteSpace(request.WikiUrl) ? null : request.WikiUrl.Trim();

        try { await _context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (IsDuplicateName(ex))
        {
            return Conflict($"Vehicle with name '{trimmedName}' already exists in this research tree.");
        }

        return NoContent();
    }

    private static VehicleDto ToDto(Vehicle vehicle) => new(vehicle.Id, vehicle.Name, vehicle.ImageUrl, vehicle.WikiUrl,
        vehicle.ResearchTreeId, vehicle.ResearchTree.NationId, vehicle.ResearchTree.Nation.Name,
        vehicle.ResearchTree.VehicleTypeId, vehicle.ResearchTree.VehicleType.Name);

    private static bool IsDuplicateName(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException
        { SqlState: "23505", ConstraintName: "IX_Vehicles_ResearchTreeId_NormalizedName" } ||
        (exception.InnerException?.Message.Contains(
            "UNIQUE constraint failed: Vehicles.ResearchTreeId, Vehicles.NormalizedName", StringComparison.Ordinal) ?? false);

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

        var hasPlayerOwnership = await _context.PlayerUnlockedVehicles
            .AnyAsync(v => v.VehicleId == id, cancellationToken);
        if (hasPlayerOwnership)
        {
            return Conflict($"Cannot delete vehicle '{id}' because it is referenced by saved player progress.");
        }

        _context.Vehicles.Remove(vehicle);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23503" } ||
            (ex.InnerException?.Message.Contains("FOREIGN KEY constraint failed", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            // A player may save this vehicle after the reference checks above.
            return Conflict($"Cannot delete vehicle '{id}' because it became referenced by tree data or saved player progress.");
        }

        return NoContent();
    }
}

