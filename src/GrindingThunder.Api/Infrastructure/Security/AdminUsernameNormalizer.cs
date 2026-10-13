namespace GrindingThunder.Api.Infrastructure.Security;

/// <summary>
/// Single normalization rule shared by authentication, account lifecycle
/// management, and bootstrap so the unique index and login lookups agree.
/// </summary>
public static class AdminUsernameNormalizer
{
    public static string Normalize(string username) => username.Trim().ToUpperInvariant();
}
