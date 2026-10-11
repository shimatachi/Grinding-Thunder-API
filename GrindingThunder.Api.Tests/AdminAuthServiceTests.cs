using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class AdminAuthServiceTests
{
    private readonly PasswordHasher _hasher = new();
    private readonly JwtTokenService _jwtService = new(Options.Create(new JwtOptions
    {
        Issuer = "GrindingThunder.Api",
        Audience = "GrindingThunder.Admin",
        SigningKey = "SecretKey_For_AdminAuthServiceTests_32CharsMinimumLength!",
        TokenLifetimeMinutes = 60
    }));

    [Fact]
    public async Task AuthenticateAsync_ValidCredentials_ReturnsSuccessWithTokenAndRoles()
    {
        var authOptions = new AdminAuthOptions
        {
            Users =
            [
                new AdminUserConfig
                {
                    Username = "editorUser",
                    Password = "EditorPassword123!",
                    Roles = [AuthRoles.Editor]
                }
            ]
        };

        var service = new AdminAuthService(Options.Create(authOptions), _hasher, _jwtService);

        var result = await service.AuthenticateAsync("editorUser", "EditorPassword123!");

        Assert.True(result.Success);
        Assert.NotNull(result.Token);
        Assert.Equal("editorUser", result.Username);
        Assert.NotNull(result.Roles);
        Assert.Contains(AuthRoles.Editor, result.Roles);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task AuthenticateAsync_SupportsPreHashedPassword()
    {
        var password = "PublisherPassword456!";
        var passwordHash = _hasher.HashPassword(password);

        var authOptions = new AdminAuthOptions
        {
            Users =
            [
                new AdminUserConfig
                {
                    Username = "publisherUser",
                    PasswordHash = passwordHash,
                    Roles = [AuthRoles.Publisher]
                }
            ]
        };

        var service = new AdminAuthService(Options.Create(authOptions), _hasher, _jwtService);

        var result = await service.AuthenticateAsync("publisherUser", password);

        Assert.True(result.Success);
        Assert.NotNull(result.Token);
        Assert.Equal("publisherUser", result.Username);
        Assert.NotNull(result.Roles);
        Assert.Contains(AuthRoles.Publisher, result.Roles);
    }

    [Fact]
    public async Task AuthenticateAsync_WrongPassword_ReturnsFailure()
    {
        var authOptions = new AdminAuthOptions
        {
            Users =
            [
                new AdminUserConfig
                {
                    Username = "adminUser",
                    Password = "CorrectPassword123!",
                    Roles = [AuthRoles.Admin]
                }
            ]
        };

        var service = new AdminAuthService(Options.Create(authOptions), _hasher, _jwtService);

        var result = await service.AuthenticateAsync("adminUser", "WrongPassword!");

        Assert.False(result.Success);
        Assert.Null(result.Token);
        Assert.Equal("Invalid username or password.", result.ErrorMessage);
    }

    [Fact]
    public async Task AuthenticateAsync_UnknownUser_ReturnsFailure()
    {
        var authOptions = new AdminAuthOptions
        {
            Users =
            [
                new AdminUserConfig
                {
                    Username = "existingUser",
                    Password = "Password123!",
                    Roles = [AuthRoles.Editor]
                }
            ]
        };

        var service = new AdminAuthService(Options.Create(authOptions), _hasher, _jwtService);

        var result = await service.AuthenticateAsync("nonExistentUser", "Password123!");

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
        var authOptions = new AdminAuthOptions();
        var service = new AdminAuthService(Options.Create(authOptions), _hasher, _jwtService);

        var result = await service.AuthenticateAsync(username, password);

        Assert.False(result.Success);
        Assert.Equal("Username and password are required.", result.ErrorMessage);
    }
}

