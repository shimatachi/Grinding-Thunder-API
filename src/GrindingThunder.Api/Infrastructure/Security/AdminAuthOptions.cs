namespace GrindingThunder.Api.Infrastructure.Security;

/// <summary>
/// Configuration options for administrative credential accounts.
/// </summary>
public class AdminAuthOptions
{
    public const string SectionName = "AdminAuth";

    /// <summary>
    /// List of configured administrative accounts.
    /// </summary>
    public List<AdminUserConfig> Users { get; set; } = new();
}

/// <summary>
/// Configuration representation of a single administrative user.
/// </summary>
public class AdminUserConfig
{
    /// <summary>
    /// Unique username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Optional plaintext password (supported for development/testing convenience).
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Salted cryptographic password hash (PBKDF2 format: &lt;salt&gt;.&lt;hash&gt;).
    /// Recommended for production credential configuration.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// Assigned administrative roles (e.g. Admin, Publisher, Editor).
    /// </summary>
    public List<string> Roles { get; set; } = new();
}

