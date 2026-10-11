using GrindingThunder.Admin.Models;

namespace GrindingThunder.Admin.Services;

/// <summary>
/// Typed client interface for communicating with the Grinding Thunder REST API.
/// </summary>
public interface IApiClient
{
    /// <summary>
    /// Gets the configured base URL of the backend API.
    /// </summary>
    string BaseUrl { get; }

    /// <summary>
    /// Authenticates administrative credentials with the backend REST API.
    /// </summary>
    Task<LoginResult> LoginAsync(string username, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves current user details from GET /api/auth/me using the attached Bearer token.
    /// </summary>
    Task<CurrentUserDto?> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies live connectivity and authentication against GET /api/auth/me.
    /// </summary>
    Task<(bool IsConnected, CurrentUserDto? User, string? ErrorMessage)> VerifySessionAsync(CancellationToken cancellationToken = default);
}

