using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Infrastructure.Persistence;
using GrindingThunder.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrindingThunder.Api.Tests;

/// <summary>
/// Tests for the explicit, idempotent first-Admin bootstrap: repeatability,
/// refusal to overwrite existing accounts, and no privilege restoration.
/// </summary>
public class AdminAccountBootstrapTests : IDisposable
{
    private readonly AdminAccountTestDatabase _database = new();
    private readonly PasswordHasher _hasher = new();

    public void Dispose() => _database.Dispose();

    private static IConfiguration ConfigWith(string? username, string? password)
    {
        var values = new Dictionary<string, string?>();
        if (username is not null) values[AdminAccountBootstrap.UsernameKey] = username;
        if (password is not null) values[AdminAccountBootstrap.PasswordKey] = password;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private Task<bool> RunBootstrapAsync(IConfiguration configuration) =>
        AdminAccountBootstrap.BootstrapAsync(
            _database.CreateContext(), _hasher, configuration, NullLogger.Instance);

    [Fact]
    public async Task Bootstrap_WithoutConfiguration_DoesNothing()
    {
        var created = await RunBootstrapAsync(ConfigWith(null, null));
        var emptyStrings = await RunBootstrapAsync(ConfigWith("", ""));
        var whitespaceStrings = await RunBootstrapAsync(ConfigWith("   ", "   "));

        Assert.False(created);
        Assert.False(emptyStrings);
        Assert.False(whitespaceStrings);
        await using var context = _database.CreateContext();
        Assert.Equal(0, await context.AdminAccounts.CountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Bootstrap_PartialConfiguration_OnlyUsername_ThrowsInvalidOperationException(string? password)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunBootstrapAsync(ConfigWith("rootadmin", password)));

        Assert.Contains(AdminAccountBootstrap.PasswordKey, exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Bootstrap_PartialConfiguration_OnlyPassword_ThrowsInvalidOperationException(string? username)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunBootstrapAsync(ConfigWith(username, "ValidBootstrapPass1!")));

        Assert.Contains(AdminAccountBootstrap.UsernameKey, exception.Message);
        Assert.DoesNotContain("ValidBootstrapPass1!", exception.Message);
    }

    [Fact]
    public async Task Bootstrap_InvalidConfiguration_UsernameExceedsMaxLength_ThrowsInvalidOperationException()
    {
        var longUsername = new string('a', 65);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunBootstrapAsync(ConfigWith(longUsername, "ValidBootstrapPass1!")));

        Assert.Contains(AdminAccountBootstrap.UsernameKey, exception.Message);
        Assert.Contains("64", exception.Message);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("short")]
    [InlineData("1234567")]
    public async Task Bootstrap_InvalidConfiguration_PasswordShorterThanMinimum_ThrowsInvalidOperationException(string shortPassword)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunBootstrapAsync(ConfigWith("rootadmin", shortPassword)));

        Assert.Contains(AdminAccountBootstrap.PasswordKey, exception.Message);
        Assert.Contains(AdminAccountService.MinPasswordLength.ToString(), exception.Message);
        Assert.DoesNotContain(shortPassword, exception.Message);
    }

    [Fact]
    public async Task Bootstrap_NonUniquePersistenceFailure_PropagatesDbUpdateException()
    {
        await using var throwingContext = _database.CreateContext(
            new ThrowingSaveChangesInterceptor(new Exception("Database disk failure")));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            AdminAccountBootstrap.BootstrapAsync(
                throwingContext, _hasher, ConfigWith("rootadmin", "BootstrapPass1!"), NullLogger.Instance));

        Assert.Contains("Database disk failure", exception.InnerException?.Message);
    }

    [Fact]
    public async Task Bootstrap_ConcurrentUniqueViolation_RecoversAndReturnsFalse()
    {
        await using var throwingContext = _database.CreateContext(
            new ThrowingSaveChangesInterceptor(new Exception("UNIQUE constraint failed: AdminAccounts.NormalizedUsername")));

        var result = await AdminAccountBootstrap.BootstrapAsync(
            throwingContext, _hasher, ConfigWith("rootadmin", "BootstrapPass1!"), NullLogger.Instance);

        Assert.False(result);
    }

    [Fact]
    public async Task Bootstrap_FirstRun_CreatesActiveAdminAccount()
    {
        var created = await RunBootstrapAsync(ConfigWith("rootadmin", "BootstrapPass1!"));

        Assert.True(created);
        await using var context = _database.CreateContext();
        var account = await context.AdminAccounts.Include(a => a.Roles).SingleAsync();
        Assert.Equal("rootadmin", account.Username);
        Assert.True(account.IsActive);
        Assert.Contains(account.Roles, r => r.Role == AuthRoles.Admin);
        Assert.True(_hasher.VerifyPassword("BootstrapPass1!", account.PasswordHash));
    }

    [Fact]
    public async Task Bootstrap_RepeatedRun_DoesNotDuplicateOrOverwrite()
    {
        Assert.True(await RunBootstrapAsync(ConfigWith("rootadmin", "BootstrapPass1!")));

        // Simulate a restart with changed bootstrap credentials.
        var second = await RunBootstrapAsync(ConfigWith("rootadmin", "ChangedPass999!"));

        Assert.False(second);
        await using var context = _database.CreateContext();
        var account = await context.AdminAccounts.SingleAsync();
        Assert.True(_hasher.VerifyPassword("BootstrapPass1!", account.PasswordHash),
            "Bootstrap must never overwrite an existing account's password hash.");
    }

    [Fact]
    public async Task Bootstrap_DisabledAdminExists_DoesNotRestorePrivileges()
    {
        Assert.True(await RunBootstrapAsync(ConfigWith("rootadmin", "BootstrapPass1!")));

        await using (var context = _database.CreateContext())
        {
            var account = await context.AdminAccounts.SingleAsync();
            account.IsActive = false;
            await context.SaveChangesAsync();
        }

        // A disabled Admin still counts: restart must not resurrect or replace it.
        var created = await RunBootstrapAsync(ConfigWith("otheradmin", "OtherPass123!"));

        Assert.False(created);
        await using var verification = _database.CreateContext();
        var accounts = await verification.AdminAccounts.ToListAsync();
        Assert.Single(accounts);
        Assert.False(accounts[0].IsActive);
    }

    [Fact]
    public async Task Bootstrap_ExistingNonAdminAccount_CreatesFirstAdmin()
    {
        await using (var context = _database.CreateContext())
        {
            var service = new AdminAccountService(context, _hasher);
            var result = await service.CreateAsync("editor", "EditorPass123!", [AuthRoles.Editor]);
            Assert.True(result.Success, result.ErrorMessage);
        }

        var created = await RunBootstrapAsync(ConfigWith("rootadmin", "BootstrapPass1!"));

        Assert.True(created);
        await using var verification = _database.CreateContext();
        Assert.Equal(2, await verification.AdminAccounts.CountAsync());
    }

    private sealed class ThrowingSaveChangesInterceptor(Exception innerException) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            throw new DbUpdateException("Simulated persistence failure", innerException);
        }
    }
}
