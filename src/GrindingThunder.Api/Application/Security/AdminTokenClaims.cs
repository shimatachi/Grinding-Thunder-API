namespace GrindingThunder.Api.Application.Security;

/// <summary>
/// Custom JWT claim types binding an administrative token to a persistent
/// account row and credential state.
/// </summary>
public static class AdminTokenClaims
{
    /// <summary>
    /// Stable <see cref="System.Guid"/> of the authenticated admin account.
    /// </summary>
    public const string AccountId = "admin_account_id";

    /// <summary>
    /// Account security stamp at token issuance. A mismatch with the stored
    /// value means the token was superseded by a credential/permission change.
    /// </summary>
    public const string SecurityStamp = "security_stamp";
}
