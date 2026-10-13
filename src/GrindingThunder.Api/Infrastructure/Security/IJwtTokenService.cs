namespace GrindingThunder.Api.Infrastructure.Security;

/// <summary>
/// Service contract for generating signed JSON Web Tokens (JWT).
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Generates a signed JWT token containing standard username claims, the stable
    /// account identity, the current security stamp, and assigned roles.
    /// Returns the raw token string and its UTC expiration timestamp.
    /// </summary>
    (string Token, DateTime ExpiresAt) GenerateToken(
        string username,
        Guid accountId,
        int securityStamp,
        IEnumerable<string> roles);
}

