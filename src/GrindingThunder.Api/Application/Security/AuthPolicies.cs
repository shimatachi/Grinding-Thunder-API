namespace GrindingThunder.Api.Application.Security;

/// <summary>
/// Defined player and administrator authorization policy names.
/// </summary>
public static class AuthPolicies
{
    /// <summary>Requires a persistent player identity rather than an administrator.</summary>
    public const string RequirePlayer = "RequirePlayer";

    /// <summary>
    /// Requires Editor role or Admin role.
    /// </summary>
    public const string RequireEditorRole = "RequireEditorRole";

    /// <summary>
    /// Requires Publisher role or Admin role.
    /// </summary>
    public const string RequirePublisherRole = "RequirePublisherRole";

    /// <summary>
    /// Requires Admin role.
    /// </summary>
    public const string RequireAdminRole = "RequireAdminRole";
}

