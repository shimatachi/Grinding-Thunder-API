namespace GrindingThunder.Api.Application.Security;

/// <summary>
/// Defined administrative roles for tech-tree management workflows.
/// </summary>
public static class AuthRoles
{
    /// <summary>
    /// Editor role: can create drafts, modify draft ranks, entries, layout, and prerequisites.
    /// </summary>
    public const string Editor = "Editor";

    /// <summary>
    /// Publisher role: can publish validated draft versions to Published status.
    /// </summary>
    public const string Publisher = "Publisher";

    /// <summary>
    /// Admin role: full administrative access including user/role management and draft deletion.
    /// </summary>
    public const string Admin = "Admin";
}

