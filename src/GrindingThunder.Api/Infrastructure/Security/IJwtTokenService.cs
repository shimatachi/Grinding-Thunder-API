namespace GrindingThunder.Api.Infrastructure.Security;

/// <summary>
/// Service contract for generating signed JSON Web Tokens (JWT).
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Generates a signed JWT token containing standard claims and assigned roles.
    /// Returns the raw token string and its UTC expiration timestamp.
    /// </summary>
    (string Token, DateTime ExpiresAt) GenerateToken(string username, IEnumerable<string> roles);
}

