using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GrindingThunder.Api.Controllers;

[ApiController]
[Route("api/player/auth")]
public class PlayerAuthController(PlayerAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(PlayerRegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.RegisterAsync(request, cancellationToken);
        if (result.Conflict)
            return Conflict(new { message = result.Error });
        return result.Account is null
            ? BadRequest(new { message = result.Error })
            : StatusCode(StatusCodes.Status201Created, result.Account);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(PlayerLoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, cancellationToken);
        return result is null ? Unauthorized(new { message = "Invalid username/email or password." }) : Ok(result);
    }

    [HttpGet("me")]
    [Authorize(Policy = AuthPolicies.RequirePlayer)]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        var accountIdClaim = User.FindFirst(PlayerTokenClaims.AccountId)?.Value;
        if (!Guid.TryParse(accountIdClaim, out var playerId))
            return Unauthorized();

        var profile = await authService.GetProfileAsync(playerId, cancellationToken);
        return profile is null ? Unauthorized() : Ok(profile);
    }
}
