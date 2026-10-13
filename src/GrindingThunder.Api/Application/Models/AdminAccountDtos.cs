namespace GrindingThunder.Api.Application.Models;

/// <summary>
/// Administrative account projection. The password hash is never exposed.
/// </summary>
public record AdminAccountDto(
    Guid Id,
    string Username,
    IReadOnlyList<string> Roles,
    bool IsActive);

/// <summary>
/// Request model for creating a persistent administrator account.
/// </summary>
public record CreateAdminAccountRequest(
    string Username,
    string Password,
    IReadOnlyList<string> Roles);

/// <summary>
/// Request model for editing username, role assignments, and active state.
/// </summary>
public record UpdateAdminAccountRequest(
    string Username,
    IReadOnlyList<string> Roles,
    bool IsActive);

/// <summary>
/// Request model for replacing an account password.
/// </summary>
public record ReplaceAdminPasswordRequest(string Password);

/// <summary>
/// Internal result of an account lifecycle operation.
/// </summary>
public record AdminAccountOperationResult(
    bool Success,
    AdminAccountDto? Account = null,
    string? ErrorMessage = null,
    bool NotFound = false,
    bool Conflict = false)
{
    public static AdminAccountOperationResult Ok(AdminAccountDto account) =>
        new(true, Account: account);

    public static AdminAccountOperationResult Fail(string errorMessage) =>
        new(false, ErrorMessage: errorMessage);

    public static AdminAccountOperationResult FailNotFound(string errorMessage) =>
        new(false, ErrorMessage: errorMessage, NotFound: true);

    public static AdminAccountOperationResult FailConflict(string errorMessage) =>
        new(false, ErrorMessage: errorMessage, Conflict: true);
}
