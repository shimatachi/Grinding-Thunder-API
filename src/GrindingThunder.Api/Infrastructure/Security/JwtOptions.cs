namespace GrindingThunder.Api.Infrastructure.Security;

/// <summary>
/// Configuration options for JWT Bearer token generation and validation.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// The expected token issuer.
    /// </summary>
    public string Issuer { get; set; } = "GrindingThunder.Api";

    /// <summary>
    /// The expected token audience.
    /// </summary>
    public string Audience { get; set; } = "GrindingThunder.Admin";

    public string PlayerAudience { get; set; } = "GrindingThunder.Player";

    /// <summary>
    /// The symmetric signing key used to sign and validate tokens (minimum 256 bits / 32 bytes).
    /// Kept out of source-controlled configuration in production.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Token lifetime in minutes (defaults to 120 minutes / 2 hours).
    /// </summary>
    public int TokenLifetimeMinutes { get; set; } = 120;
}

