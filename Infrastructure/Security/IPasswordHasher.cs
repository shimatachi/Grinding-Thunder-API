namespace GrindingThunder.Api.Infrastructure.Security;

/// <summary>
/// Service contract for hashing and verifying passwords using industry-standard cryptography.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Hashes a plaintext password using PBKDF2 (HMAC-SHA256) with a cryptographically random salt.
    /// Returns the result formatted as &lt;salt-base64&gt;.&lt;hash-base64&gt;.
    /// </summary>
    string HashPassword(string password);

    /// <summary>
    /// Verifies that a plaintext password matches a stored password hash using constant-time comparison.
    /// </summary>
    bool VerifyPassword(string password, string passwordHash);
}

