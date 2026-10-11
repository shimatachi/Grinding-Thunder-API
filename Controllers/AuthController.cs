using System.Security.Claims;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GrindingThunder.Api.Controllers;

/// <summary>
/// Controller providing administrative authentication, session profile lookup,
/// and authorization probe endpoints.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(IAdminAuthService authService) : ControllerBase
{
    private readonly IAdminAuthService _authService = authService;

    /// <summary>
    /// Authenticates an administrator using configured credentials and returns a JWT bearer token.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Username and password are required.");
        }

        var result = await _authService.AuthenticateAsync(request.Username, request.Password, cancellationToken);
        if (!result.Success)
        {
            return Unauthorized(new { message = result.ErrorMessage ?? "Invalid username or password." });
        }

        return Ok(new LoginResponse(
            Token: result.Token!,
            TokenType: "Bearer",
            ExpiresAt: result.ExpiresAt!.Value,
            Username: result.Username!,
            Roles: result.Roles!));
    }

    /// <summary>
    /// Returns the currently authenticated user's profile and roles.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public IActionResult GetCurrentUser()
    {
        var username = User.Identity?.Name ?? string.Empty;
        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct().ToList();

        return Ok(new CurrentUserResponse(username, roles));
    }

    /// <summary>
    /// Probe endpoint protected by the Editor authorization policy (Editor or Admin role).
    /// </summary>
    [HttpGet("editor-check")]
    [HttpGet("/api/admin/editor-check")]
    [Authorize(Policy = AuthPolicies.RequireEditorRole)]
    public IActionResult CheckEditorAccess()
    {
        return Ok(new
        {
            message = "Editor access granted",
            username = User.Identity?.Name
        });
    }

    /// <summary>
    /// Probe endpoint protected by the Publisher authorization policy (Publisher or Admin role).
    /// </summary>
    [HttpGet("publisher-check")]
    [HttpGet("/api/admin/publisher-check")]
    [Authorize(Policy = AuthPolicies.RequirePublisherRole)]
    public IActionResult CheckPublisherAccess()
    {
        return Ok(new
        {
            message = "Publisher access granted",
            username = User.Identity?.Name
        });
    }

    /// <summary>
    /// Probe endpoint protected by the Admin authorization policy (Admin role only).
    /// </summary>
    [HttpGet("admin-check")]
    [HttpGet("/api/admin/admin-check")]
    [Authorize(Policy = AuthPolicies.RequireAdminRole)]
    public IActionResult CheckAdminAccess()
    {
        return Ok(new
        {
            message = "Admin access granted",
            username = User.Identity?.Name
        });
    }
}

