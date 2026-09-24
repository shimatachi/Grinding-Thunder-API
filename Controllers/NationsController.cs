using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrindingThunder.Api.Infrastructure.Persistence;

namespace GrindingThunder.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NationsController(ApplicationDbContext context) : ControllerBase
{
    private readonly ApplicationDbContext _context = context;

    [HttpGet]
    public async Task<IActionResult> GetNations(CancellationToken cancellationToken)
    {
        var nations = await _context.Nations
            .AsNoTracking()
            .OrderBy(n => n.Name)
            .Select(n => new
            {
                n.Id,
                n.Name,
                n.Type,
                Ranks = n.Ranks
                    .OrderBy(r => r.RankNumber)
                    .Select(r => new
                    {
                        r.Id,
                        r.RankNumber,
                        r.RequiredVehiclesUnlocked
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return Ok(nations);
    }
}
