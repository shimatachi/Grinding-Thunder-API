using System.ComponentModel.DataAnnotations;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GrindingThunder.Api.Infrastructure.Security;

public class PlayerAuthService(
    ApplicationDbContext dbContext, IPasswordHasher passwordHasher, PlayerJwtTokenService tokenService)
{
    public async Task<PlayerRegistrationResult> RegisterAsync(
        PlayerRegisterRequest request, CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), errors, validateAllProperties: true))
            return new(null, errors[0].ErrorMessage);

        var username = request.Username.Trim();
        var email = request.Email.Trim();
        var normalizedUsername = Normalize(username);
        var normalizedEmail = Normalize(email);
        if (await dbContext.PlayerAccounts.AnyAsync(a =>
                a.NormalizedUsername == normalizedUsername || a.NormalizedEmail == normalizedEmail, cancellationToken))
            return Duplicate();

        var account = new PlayerAccount
        {
            Id = Guid.NewGuid(), Username = username, NormalizedUsername = normalizedUsername,
            Email = email, NormalizedEmail = normalizedEmail,
            PasswordHash = passwordHasher.HashPassword(request.Password)
        };
        dbContext.PlayerAccounts.Add(account);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // The unique indexes also arbitrate simultaneous registrations.
            dbContext.Entry(account).State = EntityState.Detached;
            return Duplicate();
        }
        return new(CreateTokenResponse(account));
    }

    public async Task<PlayerTokenResponse?> LoginAsync(
        PlayerLoginRequest request, CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), errors, validateAllProperties: true))
            return null;

        var identifier = Normalize(request.UsernameOrEmail);
        // Usernames exclude '@', so an email cannot ambiguously match another player's username.
        var account = await dbContext.PlayerAccounts.AsNoTracking()
            .SingleOrDefaultAsync(a => a.NormalizedUsername == identifier || a.NormalizedEmail == identifier, cancellationToken);
        if (account is null || !passwordHasher.VerifyPassword(request.Password, account.PasswordHash))
            return null;
        return CreateTokenResponse(account);
    }

    public async Task<PlayerProfileResponse?> GetProfileAsync(
        Guid playerAccountId, CancellationToken cancellationToken = default)
    {
        return await dbContext.PlayerAccounts.AsNoTracking()
            .Where(a => a.Id == playerAccountId)
            .Select(a => new PlayerProfileResponse(a.Id, a.Username, a.Email))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private PlayerTokenResponse CreateTokenResponse(PlayerAccount account)
    {
        var (token, expiresAt) = tokenService.GenerateToken(account.Id, account.Username);
        return new(account.Id, account.Username, account.Email, token, "Bearer", expiresAt);
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
    private static PlayerRegistrationResult Duplicate() => new(null, "Username or email is already registered.", Conflict: true);
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException { SqlState: "23505" } ||
        (exception.InnerException?.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) ?? false);
}
