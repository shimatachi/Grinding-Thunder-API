using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using GrindingThunder.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GrindingThunder.Api.Tests;

/// <summary>
/// Persistence-focused tests for database-backed admin accounts: hashed storage,
/// normalized uniqueness (including the database unique index), persistence
/// across scopes, last-active-Admin protection, and concurrent operations.
/// </summary>
public class AdminAccountPersistenceTests : IDisposable
{
    private readonly AdminAccountTestDatabase _database = new();
    private readonly PasswordHasher _hasher = new();

    public void Dispose() => _database.Dispose();

    private AdminAccountService CreateService(ApplicationDbContext? context = null) =>
        new(context ?? _database.CreateContext(), _hasher);

    [Fact]
    public async Task Create_PersistsHashedPassword_AndSurvivesNewContext()
    {
        var created = await CreateService().CreateAsync("persisted", "StrongPassword1!", [AuthRoles.Editor]);
        Assert.True(created.Success, created.ErrorMessage);

        // A fresh context simulates a new scope/restart.
        await using var verification = _database.CreateContext();
        var account = await verification.AdminAccounts.Include(a => a.Roles).SingleAsync();

        Assert.NotEqual("StrongPassword1!", account.PasswordHash);
        Assert.Contains('.', account.PasswordHash);
        Assert.True(_hasher.VerifyPassword("StrongPassword1!", account.PasswordHash));
        Assert.True(account.IsActive);
        Assert.Equal("PERSISTED", account.NormalizedUsername);
        Assert.Equal(AuthRoles.Editor, Assert.Single(account.Roles).Role);
    }

    [Fact]
    public async Task Create_NormalizedDuplicateUsername_ReturnsConflict()
    {
        var first = await CreateService().CreateAsync("UniqueName", "StrongPassword1!", [AuthRoles.Admin]);
        Assert.True(first.Success, first.ErrorMessage);

        var duplicate = await CreateService(_database.CreateContext())
            .CreateAsync("uniquename", "OtherPassword1!", [AuthRoles.Editor]);

        Assert.False(duplicate.Success);
        Assert.True(duplicate.Conflict);
    }

    [Fact]
    public async Task Database_UniqueIndex_RejectsNormalizedDuplicate()
    {
        await using var firstContext = _database.CreateContext();
        firstContext.AdminAccounts.Add(NewAccount("IndexCase"));
        await firstContext.SaveChangesAsync();

        await using var secondContext = _database.CreateContext();
        secondContext.AdminAccounts.Add(NewAccount("INDEXCASE"));

        await Assert.ThrowsAsync<DbUpdateException>(() => secondContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_CheckConstraint_RejectsUnsupportedRole()
    {
        await using var context = _database.CreateContext();
        var account = NewAccount("rolecase");
        account.Roles = [new AdminAccountRole { Role = "SuperUser" }];
        context.AdminAccounts.Add(account);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Update_LastActiveAdmin_CannotBeDisabledOrDemoted()
    {
        var created = await CreateService().CreateAsync("onlyadmin", "StrongPassword1!", [AuthRoles.Admin]);
        Assert.True(created.Success, created.ErrorMessage);
        var id = created.Account!.Id;

        var disable = await CreateService(_database.CreateContext())
            .UpdateAsync(id, "onlyadmin", [AuthRoles.Admin], isActive: false);
        Assert.False(disable.Success);
        Assert.True(disable.Conflict);

        var demote = await CreateService(_database.CreateContext())
            .UpdateAsync(id, "onlyadmin", [AuthRoles.Editor], isActive: true);
        Assert.False(demote.Success);
        Assert.True(demote.Conflict);

        await using var verification = _database.CreateContext();
        var account = await verification.AdminAccounts.Include(a => a.Roles).SingleAsync();
        Assert.True(account.IsActive);
        Assert.Contains(account.Roles, r => r.Role == AuthRoles.Admin);
    }

    [Fact]
    public async Task Update_SecondAdminCanBeDisabled_WhileOneRemains()
    {
        var first = await CreateService().CreateAsync("adminone", "StrongPassword1!", [AuthRoles.Admin]);
        var second = await CreateService().CreateAsync("admintwo", "StrongPassword1!", [AuthRoles.Admin]);
        Assert.True(first.Success && second.Success);

        var result = await CreateService(_database.CreateContext())
            .UpdateAsync(second.Account!.Id, "admintwo", [AuthRoles.Admin], isActive: false);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.False(result.Account!.IsActive);
    }

    [Fact]
    public async Task Update_ConcurrentLastAdminRemoval_KeepsAtLeastOneActiveAdmin()
    {
        // Shared-cache in-memory database allows two real concurrent connections.
        using var shared = new AdminAccountTestDatabase("concurrent-admin-test");
        var seed = new AdminAccountService(shared.CreateContext(), _hasher);
        var adminA = await seed.CreateAsync("adminA", "StrongPassword1!", [AuthRoles.Admin]);
        var adminB = await seed.CreateAsync("adminB", "StrongPassword1!", [AuthRoles.Admin]);
        Assert.True(adminA.Success && adminB.Success);

        var disableA = Task.Run(() => new AdminAccountService(shared.CreateContext(), _hasher)
            .UpdateAsync(adminA.Account!.Id, "adminA", [AuthRoles.Admin], isActive: false));
        var disableB = Task.Run(() => new AdminAccountService(shared.CreateContext(), _hasher)
            .UpdateAsync(adminB.Account!.Id, "adminB", [AuthRoles.Admin], isActive: false));

        var results = await Task.WhenAll(disableA, disableB);

        Assert.True(results.Count(r => r.Success) <= 1,
            "Concurrent last-Admin removals must not both succeed.");

        await using var verification = shared.CreateContext();
        var activeAdmins = await verification.AdminAccounts
            .CountAsync(a => a.IsActive && a.Roles.Any(r => r.Role == AuthRoles.Admin));
        Assert.True(activeAdmins >= 1, "At least one active Admin account must remain.");
    }

    [Fact]
    public async Task ReplacePassword_StoresNewHash_AndBumpsSecurityStamp()
    {
        var created = await CreateService().CreateAsync("pwuser", "StrongPassword1!", [AuthRoles.Editor]);
        var id = created.Account!.Id;

        var replaced = await CreateService(_database.CreateContext())
            .ReplacePasswordAsync(id, "ReplacementPass2!");
        Assert.True(replaced.Success, replaced.ErrorMessage);

        await using var verification = _database.CreateContext();
        var account = await verification.AdminAccounts.SingleAsync();
        Assert.True(_hasher.VerifyPassword("ReplacementPass2!", account.PasswordHash));
        Assert.False(_hasher.VerifyPassword("StrongPassword1!", account.PasswordHash));
        Assert.Equal(1, account.SecurityStamp);
    }

    [Fact]
    public async Task Update_InvalidRoleAssignment_ReturnsValidationFailure()
    {
        var created = await CreateService().CreateAsync("roleuser", "StrongPassword1!", [AuthRoles.Editor]);
        Assert.True(created.Success, created.ErrorMessage);

        var invalidRole = await CreateService(_database.CreateContext())
            .UpdateAsync(created.Account!.Id, "roleuser", ["SuperUser"], isActive: true);
        Assert.False(invalidRole.Success);
        Assert.False(invalidRole.Conflict);

        var emptyRoles = await CreateService(_database.CreateContext())
            .UpdateAsync(created.Account!.Id, "roleuser", [], isActive: true);
        Assert.False(emptyRoles.Success);

        var createdInvalid = await CreateService(_database.CreateContext())
            .CreateAsync("another", "StrongPassword1!", [AuthRoles.Admin, AuthRoles.Admin]);
        Assert.False(createdInvalid.Success);
    }

    private AdminAccount NewAccount(string username) => new()
    {
        Id = Guid.NewGuid(),
        Username = username,
        NormalizedUsername = AdminUsernameNormalizer.Normalize(username),
        PasswordHash = _hasher.HashPassword("StrongPassword1!"),
        IsActive = true,
        SecurityStamp = 0,
        Roles = [new AdminAccountRole { Role = AuthRoles.Editor }]
    };
}
