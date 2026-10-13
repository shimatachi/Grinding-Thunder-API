using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class AdminAuthServiceTests : IDisposable
{
    private readonly AdminAccountTestDatabase _database = new();
    private readonly PasswordHasher _hasher = new();
    private readonly JwtTokenService _jwtService = new(Options.Create(new JwtOptions
    {
        Issuer = "GrindingThunder.Api",
        Audience = "GrindingThunder.Admin",
        SigningKey = "SecretKey_For_AdminAuthServiceTests_32CharsMinimumLength!",
        TokenLifetimeMinutes = 60
    }));

    public void Dispose() => _database.Dispose();

    private AdminAuthService CreateService() =>
        new(_database.CreateContext(), _hasher, _jwtService);

    private async Task SeedAccountAsync(
        string username, string password, string role, bool isActive = true)
    {
        await using var context = _database.CreateContext();
        var service = new AdminAccountService(context, _hasher);
        var result = await service.CreateAsync(username, password, [role]);
        Assert.True(result.Success, result.ErrorMessage);

        if (!isActive)
        {
            var account = context.AdminAccounts.Single(a => a.Username == username);
            account.IsActive = false;
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task AuthenticateAsync_ValidCredentials_ReturnsSuccessWithTokenAndRoles()
    {
        await SeedAccountAsync("editorUser", "EditorPassword123!", AuthRoles.Editor);

        var result = await CreateService().AuthenticateAsync("editorUser", "EditorPassword123!");

        Assert.True(result.Success);
        Assert.NotNull(result.Token);
        Assert.Equal("editorUser", result.Username);
        Assert.NotNull(result.Roles);
        Assert.Contains(AuthRoles.Editor, result.Roles);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task AuthenticateAsync_UsernameMatch_IsCaseInsensitive()
    {
        await SeedAccountAsync("publisherUser", "PublisherPassword456!", AuthRoles.Publisher);

        var result = await CreateService().AuthenticateAsync("PUBLISHERUSER", "PublisherPassword456!");

        Assert.True(result.Success);
        Assert.Equal("publisherUser", result.Username);
        Assert.NotNull(result.Roles);
        Assert.Contains(AuthRoles.Publisher, result.Roles);
    }

    [Fact]
    public async Task AuthenticateAsync_WrongPassword_ReturnsFailure()
    {
        await SeedAccountAsync("adminUser", "CorrectPassword123!", AuthRoles.Admin);

        var result = await CreateService().AuthenticateAsync("adminUser", "WrongPassword!");

        Assert.False(result.Success);
        Assert.Null(result.Token);
        Assert.Equal("Invalid username or password.", result.ErrorMessage);
    }

    [Fact]
    public async Task AuthenticateAsync_UnknownUser_ReturnsFailure()
    {
        await SeedAccountAsync("existingUser", "Password123!", AuthRoles.Editor);

        var result = await CreateService().AuthenticateAsync("nonExistentUser", "Password123!");

        Assert.False(result.Success);
        Assert.Null(result.Token);
        Assert.Equal("Invalid username or password.", result.ErrorMessage);
    }

    [Fact]
    public async Task AuthenticateAsync_DisabledAccount_ReturnsFailure()
    {
        await SeedAccountAsync("disabledUser", "Password123!", AuthRoles.Editor, isActive: false);

        var result = await CreateService().AuthenticateAsync("disabledUser", "Password123!");

        Assert.False(result.Success);
        Assert.Null(result.Token);
        Assert.Equal("Invalid username or password.", result.ErrorMessage);
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("user", "")]
    [InlineData("   ", "   ")]
    public async Task AuthenticateAsync_EmptyUsernameOrPassword_ReturnsFailure(string username, string password)
    {
        var result = await CreateService().AuthenticateAsync(username, password);

        Assert.False(result.Success);
        Assert.Equal("Username and password are required.", result.ErrorMessage);
    }
}
