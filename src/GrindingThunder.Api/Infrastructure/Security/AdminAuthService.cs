using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GrindingThunder.Api.Infrastructure.Security;

/// <summary>
/// Service responsible for authenticating administrative users against persistent
/// database-backed accounts with PBKDF2 password hashes. Disabled accounts and
/// unknown usernames share one generic failure to avoid account enumeration.
/// </summary>
public class AdminAuthService(
    ApplicationDbContext dbContext,
    IPasswordHasher passwordHasher,
    IJwtTokenService tokenService) : IAdminAuthService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IPasswordHasher _passwordHasher = passwordHasher;
    private readonly IJwtTokenService _tokenService = tokenService;

    public async Task<AdminAuthResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return new AdminAuthResult(
                Success: false,
                ErrorMessage: "Username and password are required.");
        }

        var normalizedUsername = AdminUsernameNormalizer.Normalize(username);
        var account = await _dbContext.AdminAccounts
            .Include(a => a.Roles)
            .FirstOrDefaultAsync(a => a.NormalizedUsername == normalizedUsername, cancellationToken);

        if (account == null || !account.IsActive ||
            !_passwordHasher.VerifyPassword(password, account.PasswordHash))
        {
            return new AdminAuthResult(
                Success: false,
                ErrorMessage: "Invalid username or password.");
        }

        var roles = account.Roles.Select(r => r.Role).Order().ToList();
        if (roles.Count == 0)
        {
            return new AdminAuthResult(
                Success: false,
                ErrorMessage: "Invalid username or password.");
        }

        var (token, expiresAt) = _tokenService.GenerateToken(
            account.Username, account.Id, account.SecurityStamp, roles);

        return new AdminAuthResult(
            Success: true,
            Token: token,
            ExpiresAt: expiresAt,
            Username: account.Username,
            Roles: roles);
    }
}
