using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GrindingThunder.Api.Controllers;

/// <summary>
/// Admin-only lifecycle management of persistent administrator accounts.
/// Password hashes are never exposed or accepted through this controller.
/// </summary>
[ApiController]
[Route("api/admin/accounts")]
[Authorize(Policy = AuthPolicies.RequireAdminRole)]
public class AdminAccountsController(IAdminAccountService accountService) : ControllerBase
{
    private readonly IAdminAccountService _accountService = accountService;

    /// <summary>
    /// Lists all administrator accounts.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListAccounts(CancellationToken cancellationToken)
    {
        var accounts = await _accountService.ListAsync(cancellationToken);
        return Ok(accounts);
    }

    /// <summary>
    /// Returns one administrator account by stable ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetAccount(Guid id, CancellationToken cancellationToken)
    {
        var account = await _accountService.GetAsync(id, cancellationToken);
        return account is null ? NotFound(new { message = "Account was not found." }) : Ok(account);
    }

    /// <summary>
    /// Creates a new active administrator account.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateAccount(
        [FromBody] CreateAdminAccountRequest request, CancellationToken cancellationToken)
    {
        var result = await _accountService.CreateAsync(
            request.Username, request.Password, request.Roles ?? [], cancellationToken);

        return ToActionResult(result, successStatus: account =>
            CreatedAtAction(nameof(GetAccount), new { id = account.Id }, account));
    }

    /// <summary>
    /// Replaces username, role assignments, and active state of an account.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateAccount(
        Guid id, [FromBody] UpdateAdminAccountRequest request, CancellationToken cancellationToken)
    {
        var result = await _accountService.UpdateAsync(
            id, request.Username, request.Roles ?? [], request.IsActive, cancellationToken);

        return ToActionResult(result, successStatus: _ => NoContent());
    }

    /// <summary>
    /// Replaces an account password, invalidating previously issued tokens.
    /// </summary>
    [HttpPut("{id:guid}/password")]
    public async Task<IActionResult> ReplacePassword(
        Guid id, [FromBody] ReplaceAdminPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _accountService.ReplacePasswordAsync(id, request.Password, cancellationToken);

        return ToActionResult(result, successStatus: _ => NoContent());
    }

    private IActionResult ToActionResult(
        AdminAccountOperationResult result,
        Func<AdminAccountDto, IActionResult> successStatus)
    {
        if (result.Success && result.Account is not null)
        {
            return successStatus(result.Account);
        }

        var error = new { message = result.ErrorMessage ?? "Account operation failed." };

        if (result.NotFound)
        {
            return NotFound(error);
        }

        if (result.Conflict)
        {
            return Conflict(error);
        }

        return BadRequest(error);
    }
}
