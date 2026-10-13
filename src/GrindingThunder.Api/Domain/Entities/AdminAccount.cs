namespace GrindingThunder.Api.Domain.Entities;

/// <summary>
/// Persistent administrator account identity, deliberately separate from any
/// future player identity. The password hash, roles, and active state are
/// managed exclusively through the administrative account lifecycle service.
/// </summary>
public class AdminAccount
{
    /// <summary>
    /// Stable account identity. Usernames can change; this ID does not.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Display/login username as entered. Login matching is case-insensitive
    /// via <see cref="NormalizedUsername"/>.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Uppercase-normalized username with a unique database index enforcing
    /// case-insensitive uniqueness.
    /// </summary>
    public string NormalizedUsername { get; set; } = string.Empty;

    /// <summary>
    /// Salted PBKDF2 password hash (&lt;salt&gt;.&lt;hash&gt;). Never exposed through the API.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Disabled accounts cannot authenticate and their existing tokens are rejected.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Incremented whenever credentials, username, roles, or active state change.
    /// Tokens carrying a superseded stamp are rejected during validation.
    /// </summary>
    public int SecurityStamp { get; set; }

    /// <summary>
    /// Assigned administrative roles (nonempty, values restricted to the
    /// supported Admin/Publisher/Editor set).
    /// </summary>
    public List<AdminAccountRole> Roles { get; set; } = [];
}
