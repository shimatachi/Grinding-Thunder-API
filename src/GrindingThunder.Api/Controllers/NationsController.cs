using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;

namespace GrindingThunder.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NationsController(ApplicationDbContext context) : ControllerBase
{
    private readonly ApplicationDbContext _context = context;

    [HttpGet]
    public async Task<IActionResult> GetNations(
        [FromQuery] bool includeDrafts = false,
        CancellationToken cancellationToken = default)
    {
        // Draft discovery is a back-office concern. The anonymous/default catalog
        // is deliberately publication-filtered at the database boundary.
        if (includeDrafts && !User.HasClaim(claim => claim.Type == AdminTokenClaims.AccountId))
        {
            return Forbid();
        }

        var publishedOnly = !includeDrafts;
        var nations = await _context.Nations
            .AsNoTracking()
            .AsSplitQuery()
            .Where(n => !publishedOnly || n.ResearchTrees.Any(rt =>
                rt.Versions.Any(v => v.Status == ResearchTreeVersionStatus.Published)))
            .OrderBy(n => n.Name)
            .Select(n => new
            {
                n.Id,
                n.Name,
                ResearchTrees = n.ResearchTrees
                    .Where(rt => !publishedOnly || rt.Versions.Any(v =>
                        v.Status == ResearchTreeVersionStatus.Published))
                    .OrderBy(rt => rt.VehicleType.Name)
                    .Select(rt => new
                    {
                        rt.Id,
                        rt.VehicleTypeId,
                        VehicleTypeName = rt.VehicleType.Name,
                        Versions = rt.Versions
                            .Where(v => !publishedOnly || v.Status == ResearchTreeVersionStatus.Published)
                            .OrderByDescending(v => v.GameUpdate.ReleaseDate)
                            .ThenByDescending(v => v.GameUpdate.Version)
                            .Select(v => new
                            {
                                v.Id,
                                v.GameUpdateId,
                                v.Status,
                                UpdateVersion = v.GameUpdate.Version,
                                TreeRanks = v.TreeRanks
                                    .OrderBy(r => r.RankNumber)
                                    .Select(r => new
                                    {
                                        r.Id,
                                        r.RankNumber,
                                        r.RequiredVehiclesUnlocked
                                    })
                                    .ToList()
                            })
                            .ToList()
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return Ok(nations);
    }
}
