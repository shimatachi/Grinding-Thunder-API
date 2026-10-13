using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GrindingThunder.Api.Controllers;

[ApiController]
[Route("api/player/progress")]
[Authorize(Policy = AuthPolicies.RequirePlayer)]
public class PlayerProgressController(PlayerProgressService progressService) : ControllerBase
{
    // The authenticated account claim is the only account selector; clients cannot choose another player.
    private Guid PlayerId => Guid.Parse(User.FindFirst(PlayerTokenClaims.AccountId)!.Value);

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var progress = await progressService.GetAsync(PlayerId, cancellationToken);
        return progress is null ? NotFound() : Ok(progress);
    }

    [HttpPut]
    public async Task<IActionResult> Update(UpdatePlayerProgressRequest request, CancellationToken cancellationToken)
    {
        var result = await progressService.UpdateAsync(PlayerId, request, cancellationToken);
        if (result.NotFound)
            return NotFound();
        if (result.Conflict)
            return Conflict(new { message = result.Error });
        return result.Progress is null ? BadRequest(new { message = result.Error }) : Ok(result.Progress);
    }
}
