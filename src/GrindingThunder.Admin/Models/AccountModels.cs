using System.ComponentModel.DataAnnotations;

namespace GrindingThunder.Admin.Models;

/// <summary>
/// HTTP response payload received from the account lifecycle endpoints.
/// The API never returns password hashes.
/// </summary>
public record AdminAccountDto(Guid Id, string Username, IReadOnlyList<string> Roles, bool IsActive);

/// <summary>
/// HTTP request payload sent to POST /api/admin/accounts.
/// </summary>
public record CreateAdminAccountRequest(string Username, string Password, IReadOnlyList<string> Roles);

/// <summary>
/// HTTP request payload sent to PUT /api/admin/accounts/{id}.
/// </summary>
public record UpdateAdminAccountRequest(string Username, IReadOnlyList<string> Roles, bool IsActive);

/// <summary>
/// HTTP request payload sent to PUT /api/admin/accounts/{id}/password.
/// </summary>
public record ReplaceAdminPasswordRequest(string Password);

/// <summary>
/// Roles the backend accepts for administrator accounts.
/// </summary>
public static class SupportedAdminRoles
{
    public static readonly string[] All = ["Admin", "Publisher", "Editor"];
}

/// <summary>
/// Model bound to the account creation form.
/// </summary>
public class AdminAccountCreateInputModel
{
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(64, ErrorMessage = "Username must be at most 64 characters long.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters long.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public List<string> Roles { get; set; } = [];
}

/// <summary>
/// Model bound to the account edit form.
/// </summary>
public class AdminAccountEditInputModel
{
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(64, ErrorMessage = "Username must be at most 64 characters long.")]
    public string Username { get; set; } = string.Empty;

    public List<string> Roles { get; set; } = [];

    public bool IsActive { get; set; }
}

/// <summary>
/// Model bound to the password replacement form.
/// </summary>
public class AdminPasswordInputModel
{
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
}
