using System.ComponentModel.DataAnnotations;

namespace GrindingThunder.Admin.Models;

/// <summary>
/// Model bound to the login page form.
/// </summary>
public class LoginInputModel
{
    [Required(ErrorMessage = "Username is required.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}

/// <summary>
/// HTTP request payload sent to POST /api/auth/login.
/// </summary>
public record LoginRequest(string Username, string Password);

/// <summary>
/// HTTP response payload received from POST /api/auth/login.
/// </summary>
public record LoginResponse(
    string Token,
    string TokenType,
    DateTime ExpiresAt,
    string Username,
    IReadOnlyList<string> Roles);

/// <summary>
/// HTTP response payload received from GET /api/auth/me.
/// </summary>
public record CurrentUserDto(
    string Username,
    IReadOnlyList<string> Roles);

/// <summary>
/// HTTP error response payload returned by API when authentication fails.
/// </summary>
public record ApiErrorResponse(string? Message);

/// <summary>
/// Result returned by the client-side login operation.
/// </summary>
public record LoginResult(
    bool Success,
    string? Token = null,
    DateTime? ExpiresAt = null,
    string? Username = null,
    IReadOnlyList<string>? Roles = null,
    string? ErrorMessage = null);

