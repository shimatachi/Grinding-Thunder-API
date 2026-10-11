using GrindingThunder.Api.Application.Models;

namespace GrindingThunder.Api.Infrastructure.Security;

/// <summary>
/// Service contract for validating administrator credentials and generating authentication responses.
/// </summary>
public interface IAdminAuthService
{
    /// <summary>
    /// Authenticates an administrator by username and password.
    /// Returns an AdminAuthResult indicating success/failure and containing token and role claims.
    /// </summary>
    Task<AdminAuthResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default);
}

