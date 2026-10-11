namespace GrindingThunder.Api.Application.Security;

/// <summary>
/// Defined administrative authorization policy names.
/// </summary>
public static class AuthPolicies
{
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

