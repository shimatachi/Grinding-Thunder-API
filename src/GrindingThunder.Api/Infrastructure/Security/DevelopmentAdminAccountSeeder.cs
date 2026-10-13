using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GrindingThunder.Api.Infrastructure.Security;

/// <summary>
/// Deliberate, environment-limited seeding of well-known development/test
/// administrator accounts with hashed passwords. Runs only when the caller
/// explicitly invokes it (Development startup and test fixtures) and never
/// overwrites existing account state: it does nothing once any account exists.
/// </summary>
public static class DevelopmentAdminAccountSeeder
{
    public const string AdminUsername = "admin";
    public const string AdminPassword = "AdminPass123!";
    public const string PublisherUsername = "publisher";
    public const string PublisherPassword = "PublisherPass123!";
    public const string EditorUsername = "editor";
    public const string EditorPassword = "EditorPass123!";

    public static async Task SeedAsync(
        ApplicationDbContext dbContext,
        IPasswordHasher passwordHasher,
        CancellationToken cancellationToken = default)
    {
        if (await dbContext.AdminAccounts.AnyAsync(cancellationToken))
        {
            return;
        }

        dbContext.AdminAccounts.AddRange(
            Create(AdminUsername, AdminPassword, AuthRoles.Admin, passwordHasher),
            Create(PublisherUsername, PublisherPassword, AuthRoles.Publisher, passwordHasher),
            Create(EditorUsername, EditorPassword, AuthRoles.Editor, passwordHasher));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static AdminAccount Create(
        string username, string password, string role, IPasswordHasher passwordHasher) =>
        new()
        {
            Id = Guid.NewGuid(),
            Username = username,
            NormalizedUsername = AdminUsernameNormalizer.Normalize(username),
            PasswordHash = passwordHasher.HashPassword(password),
            IsActive = true,
            SecurityStamp = 0,
            Roles = [new AdminAccountRole { Role = role }]
        };
}
