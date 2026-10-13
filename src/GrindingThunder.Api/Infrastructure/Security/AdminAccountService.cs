using System.Data;
using System.Data.Common;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GrindingThunder.Api.Infrastructure.Security;

/// <summary>
/// Database-backed administrator account lifecycle management. Enforces supported
/// nonempty role assignments, case-insensitive username uniqueness, and the
/// invariant that the system always retains at least one active Admin account.
/// Guarded mutations run inside serializable transactions so concurrent lifecycle
/// operations cannot jointly violate that invariant.
/// </summary>
public class AdminAccountService(
    ApplicationDbContext dbContext,
    IPasswordHasher passwordHasher) : IAdminAccountService
{
    public const int MinPasswordLength = 8;

    private static readonly string[] SupportedRoles = [AuthRoles.Admin, AuthRoles.Publisher, AuthRoles.Editor];

    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IPasswordHasher _passwordHasher = passwordHasher;

    public async Task<IReadOnlyList<AdminAccountDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await _dbContext.AdminAccounts
            .AsNoTracking()
            .Include(a => a.Roles)
            .OrderBy(a => a.NormalizedUsername)
            .ToListAsync(cancellationToken);

        return accounts.Select(ToDto).ToList();
    }

    public async Task<AdminAccountDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await _dbContext.AdminAccounts
            .AsNoTracking()
            .Include(a => a.Roles)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        return account is null ? null : ToDto(account);
    }

    public async Task<AdminAccountOperationResult> CreateAsync(
        string username,
        string password,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default)
    {
        var validationError = Validate(username, password, roles);
        if (validationError is not null)
        {
            return AdminAccountOperationResult.Fail(validationError);
        }

        var normalizedUsername = AdminUsernameNormalizer.Normalize(username);

        var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        if (await _dbContext.AdminAccounts.AnyAsync(
                a => a.NormalizedUsername == normalizedUsername, cancellationToken))
        {
            return AdminAccountOperationResult.FailConflict(
                $"Username '{username.Trim()}' is already taken.");
        }

        var account = new AdminAccount
        {
            Id = Guid.NewGuid(),
            Username = username.Trim(),
            NormalizedUsername = normalizedUsername,
            PasswordHash = _passwordHasher.HashPassword(password),
            IsActive = true,
            SecurityStamp = 0,
            Roles = roles.Select(role => new AdminAccountRole { Role = role }).ToList()
        };

        _dbContext.AdminAccounts.Add(account);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Concurrent create raced the application-level uniqueness check.
            return AdminAccountOperationResult.FailConflict(
                $"Username '{username.Trim()}' is already taken.");
        }

        return AdminAccountOperationResult.Ok(ToDto(account));
    }

    public async Task<AdminAccountOperationResult> UpdateAsync(
        Guid id,
        string username,
        IReadOnlyList<string> roles,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var validationError = Validate(username, password: null, roles);
        if (validationError is not null)
        {
            return AdminAccountOperationResult.Fail(validationError);
        }

        var normalizedUsername = AdminUsernameNormalizer.Normalize(username);

        var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        var account = await _dbContext.AdminAccounts
            .Include(a => a.Roles)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (account is null)
        {
            return AdminAccountOperationResult.FailNotFound("Account was not found.");
        }

        if (!string.Equals(account.NormalizedUsername, normalizedUsername, StringComparison.Ordinal) &&
            await _dbContext.AdminAccounts.AnyAsync(
                a => a.NormalizedUsername == normalizedUsername, cancellationToken))
        {
            return AdminAccountOperationResult.FailConflict(
                $"Username '{username.Trim()}' is already taken.");
        }

        var losesLastActiveAdmin =
            account.IsActive &&
            account.Roles.Any(r => r.Role == AuthRoles.Admin) &&
            (!isActive || !roles.Contains(AuthRoles.Admin));

        if (losesLastActiveAdmin)
        {
            var anotherActiveAdminExists = await _dbContext.AdminAccounts
                .AnyAsync(a => a.Id != id && a.IsActive &&
                               a.Roles.Any(r => r.Role == AuthRoles.Admin), cancellationToken);

            if (!anotherActiveAdminExists)
            {
                return AdminAccountOperationResult.FailConflict(
                    "This change would leave the system without an active Admin account.");
            }
        }

        account.Username = username.Trim();
        account.NormalizedUsername = normalizedUsername;
        account.IsActive = isActive;
        account.SecurityStamp++;

        _dbContext.AdminAccountRoles.RemoveRange(account.Roles);
        account.Roles = roles.Select(role => new AdminAccountRole { AdminAccountId = id, Role = role }).ToList();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return AdminAccountOperationResult.FailConflict(
                $"Username '{username.Trim()}' is already taken.");
        }
        catch (DbException)
        {
            // Serializable transaction aborted because of a concurrent lifecycle
            // operation (for example two simultaneous last-Admin removals).
            return AdminAccountOperationResult.FailConflict(
                "The account could not be updated because of a concurrent change. Retry the operation.");
        }

        return AdminAccountOperationResult.Ok(ToDto(account));
    }

    public async Task<AdminAccountOperationResult> ReplacePasswordAsync(
        Guid id,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < MinPasswordLength)
        {
            return AdminAccountOperationResult.Fail(
                $"Password must be at least {MinPasswordLength} characters long.");
        }

        var account = await _dbContext.AdminAccounts
            .Include(a => a.Roles)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (account is null)
        {
            return AdminAccountOperationResult.FailNotFound("Account was not found.");
        }

        account.PasswordHash = _passwordHasher.HashPassword(newPassword);
        account.SecurityStamp++;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return AdminAccountOperationResult.Ok(ToDto(account));
    }

    private static string? Validate(string username, string? password, IReadOnlyList<string> roles)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return "Username is required.";
        }

        if (username.Trim().Length > 64)
        {
            return "Username must be at most 64 characters long.";
        }

        if (password is not null &&
            (string.IsNullOrWhiteSpace(password) || password.Length < MinPasswordLength))
        {
            return $"Password must be at least {MinPasswordLength} characters long.";
        }

        if (roles is null || roles.Count == 0)
        {
            return "At least one role must be assigned.";
        }

        if (roles.Any(role => !SupportedRoles.Contains(role)))
        {
            return $"Roles must be selected from: {string.Join(", ", SupportedRoles)}.";
        }

        if (roles.Distinct().Count() != roles.Count)
        {
            return "Roles must not contain duplicates.";
        }

        return null;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException { SqlState: "23505" } ||
        (exception.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) ?? false);

    private static AdminAccountDto ToDto(AdminAccount account) =>
        new(account.Id, account.Username, account.Roles.Select(r => r.Role).Order().ToList(), account.IsActive);
}
