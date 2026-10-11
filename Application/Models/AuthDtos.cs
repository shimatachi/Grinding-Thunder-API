namespace GrindingThunder.Api.Application.Models;

/// <summary>
/// Request model for administrative authentication.
/// </summary>
public record LoginRequest(string Username, string Password);

/// <summary>
/// Response model returned upon successful administrative authentication.
/// </summary>
public record LoginResponse(
    string Token,
    string TokenType,
    DateTime ExpiresAt,
    string Username,
    IReadOnlyList<string> Roles);

/// <summary>
/// Response model representing the authenticated administrator profile.
/// </summary>
public record CurrentUserResponse(
    string Username,
    IReadOnlyList<string> Roles);

/// <summary>
/// Internal result record returned by the administrative authentication service.
/// </summary>
public record AdminAuthResult(
    bool Success,
    string? Token = null,
    DateTime? ExpiresAt = null,
    string? Username = null,
    IReadOnlyList<string>? Roles = null,
    string? ErrorMessage = null);

