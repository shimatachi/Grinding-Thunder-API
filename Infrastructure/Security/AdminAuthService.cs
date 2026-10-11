using GrindingThunder.Api.Application.Models;
using Microsoft.Extensions.Options;

namespace GrindingThunder.Api.Infrastructure.Security;

/// <summary>
/// Service responsible for authenticating administrative users against configured credentials,
/// supporting both PBKDF2 hashed passwords and development convenience credentials.
/// </summary>
public class AdminAuthService(
    IOptions<AdminAuthOptions> authOptions,
    IPasswordHasher passwordHasher,
    IJwtTokenService tokenService) : IAdminAuthService
{
    private readonly AdminAuthOptions _authOptions = authOptions.Value;
    private readonly IPasswordHasher _passwordHasher = passwordHasher;
    private readonly IJwtTokenService _tokenService = tokenService;

    public Task<AdminAuthResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return Task.FromResult(new AdminAuthResult(
                Success: false,
                ErrorMessage: "Username and password are required."));
        }

        var user = _authOptions.Users.FirstOrDefault(u =>
            string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

        if (user == null)
        {
            return Task.FromResult(new AdminAuthResult(
                Success: false,
                ErrorMessage: "Invalid username or password."));
        }

        bool passwordValid = false;

        if (!string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            passwordValid = _passwordHasher.VerifyPassword(password, user.PasswordHash);
        }
        else if (!string.IsNullOrWhiteSpace(user.Password))
        {
            // Direct comparison for local development/testing convenience
            passwordValid = string.Equals(user.Password, password, StringComparison.Ordinal);
        }

        if (!passwordValid)
        {
            return Task.FromResult(new AdminAuthResult(
                Success: false,
                ErrorMessage: "Invalid username or password."));
        }

        var (token, expiresAt) = _tokenService.GenerateToken(user.Username, user.Roles);

        return Task.FromResult(new AdminAuthResult(
            Success: true,
            Token: token,
            ExpiresAt: expiresAt,
            Username: user.Username,
            Roles: user.Roles));
    }
}

