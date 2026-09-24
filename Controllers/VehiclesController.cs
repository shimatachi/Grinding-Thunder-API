using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        var vehicles = await _context.Vehicles
            .AsNoTracking()
            .Select(v => new
            {
                v.Id,
                v.RankId,
                v.Name,
                v.ImageUrl,
                v.RpCost,
                v.SlCost,
                v.IsFolderParent,
                v.FolderParentId,
                v.TreeColumn,
                v.TreeRow,
                Prerequisites = v.Prerequisites.Select(p => p.PrerequisiteVehicleId).ToList()
            })
            .ToListAsync();

        return Ok(vehicles);
    }

    [HttpGet("tree")]
    public async Task<IActionResult> GetVehicleTree(
        [FromQuery] Guid nationId,
        [FromQuery] string? type,
        CancellationToken cancellationToken)
    {
        if (nationId == Guid.Empty)
        {
            return BadRequest("nationId is required.");
        }

        var query = _context.Vehicles
            .AsNoTracking()
            .Where(v => v.Rank.NationId == nationId);

        if (!string.IsNullOrWhiteSpace(type))
        {
            var normalizedType = type.ToLowerInvariant();
            query = query.Where(v => v.Rank.Nation.Type.ToLower() == normalizedType);
        }

        var vehicles = await query
            .Select(v => new
            {
                v.Id,
                v.RankId,
                v.Name,
                v.ImageUrl,
                v.RpCost,
                v.SlCost,
                v.IsFolderParent,
                v.FolderParentId,
                v.TreeColumn,
                v.TreeRow,
                PrerequisiteIds = v.Prerequisites
                    .Select(p => p.PrerequisiteVehicleId)
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return Ok(vehicles);
    }
}
