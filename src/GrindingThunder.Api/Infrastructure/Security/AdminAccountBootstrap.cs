using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GrindingThunder.Api.Infrastructure.Security;

/// <summary>
/// Safe, explicit, idempotent bootstrap of the first administrator account from
/// external configuration or user secrets. The bootstrap never imports configured
/// users, never overwrites an existing account, and cannot restore privileges on
/// restart: once any Admin-role account exists it does nothing. Credentials are
/// never logged.
/// </summary>
public static class AdminAccountBootstrap
{
    public const string UsernameKey = "AdminBootstrap:Username";
    public const string PasswordKey = "AdminBootstrap:Password";

    /// <summary>
    /// Creates the first Admin account when bootstrap credentials are configured
    /// and no Admin-role account exists. Returns true when an account was created.
    /// </summary>
    public static async Task<bool> BootstrapAsync(
        ApplicationDbContext dbContext,
        IPasswordHasher passwordHasher,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var username = configuration[UsernameKey];
        var password = configuration[PasswordKey];

        if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation(
                "Admin bootstrap skipped: '{UsernameKey}'/'{PasswordKey}' are not configured.",
                UsernameKey, PasswordKey);
            return false;
        }

        // Skipping when ANY Admin-role account exists (including disabled ones)
        // prevents both duplicate creation and privilege restoration on restart.
        var adminExists = await dbContext.AdminAccounts
            .AnyAsync(a => a.Roles.Any(r => r.Role == AuthRoles.Admin), cancellationToken);

        if (adminExists)
        {
            logger.LogInformation("Admin bootstrap skipped: an Admin account already exists.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException(
                $"Admin bootstrap configuration is invalid: '{UsernameKey}' is required when bootstrap credentials are configured.");
        }

        if (username.Trim().Length > 64)
        {
            throw new InvalidOperationException(
                $"Admin bootstrap configuration is invalid: '{UsernameKey}' must be at most 64 characters long.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                $"Admin bootstrap configuration is invalid: '{PasswordKey}' is required when bootstrap credentials are configured.");
        }

        if (password.Length < AdminAccountService.MinPasswordLength)
        {
            throw new InvalidOperationException(
                $"Admin bootstrap configuration is invalid: '{PasswordKey}' must be at least {AdminAccountService.MinPasswordLength} characters long.");
        }

        var account = new AdminAccount
        {
            Id = Guid.NewGuid(),
            Username = username.Trim(),
            NormalizedUsername = AdminUsernameNormalizer.Normalize(username),
            PasswordHash = passwordHasher.HashPassword(password),
            IsActive = true,
            SecurityStamp = 0,
            Roles = [new AdminAccountRole { Role = AuthRoles.Admin }]
        };

        dbContext.AdminAccounts.Add(account);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A concurrent bootstrap created an account with the same normalized
            // username first; the unique index keeps exactly one winner.
            logger.LogWarning(
                "Admin bootstrap skipped: username '{Username}' is already taken.", account.Username);
            return false;
        }

        logger.LogInformation(
            "Admin bootstrap created the first Admin account '{Username}'.", account.Username);
        return true;
    }

    internal static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException { SqlState: "23505" } ||
        (exception.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) ?? false);
}
