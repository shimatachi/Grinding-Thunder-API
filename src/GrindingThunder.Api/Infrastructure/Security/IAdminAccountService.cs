using GrindingThunder.Api.Application.Models;

namespace GrindingThunder.Api.Infrastructure.Security;

/// <summary>
/// Service contract for persistent administrator account lifecycle management.
/// </summary>
public interface IAdminAccountService
{
    /// <summary>
    /// Lists all administrator accounts ordered by username. Password hashes are never returned.
    /// </summary>
    Task<IReadOnlyList<AdminAccountDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one account by stable ID, or null when it does not exist.
    /// </summary>
    Task<AdminAccountDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new active account with a hashed password. Usernames must be
    /// unique case-insensitively; roles must be nonempty and supported.
    /// </summary>
    Task<AdminAccountOperationResult> CreateAsync(
        string username,
        string password,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces username, roles, and active state. Never allows an operation that
    /// would leave the system without an active Admin account.
    /// </summary>
    Task<AdminAccountOperationResult> UpdateAsync(
        Guid id,
        string username,
        IReadOnlyList<string> roles,
        bool isActive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the account password with a new hash, invalidating existing tokens.
    /// </summary>
    Task<AdminAccountOperationResult> ReplacePasswordAsync(
        Guid id,
        string newPassword,
        CancellationToken cancellationToken = default);
}
