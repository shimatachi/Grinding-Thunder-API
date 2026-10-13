using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GrindingThunder.Api.Application.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GrindingThunder.Api.Infrastructure.Security;

/// <summary>Uses existing signing infrastructure with a separate player audience and no administrator roles.</summary>
public class PlayerJwtTokenService(IOptions<JwtOptions> options)
{
    public (string Token, DateTime ExpiresAt) GenerateToken(Guid playerAccountId, string username)
    {
        if (playerAccountId == Guid.Empty)
            throw new ArgumentException("A stable player account ID is required.", nameof(playerAccountId));
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.SigningKey))
            throw new InvalidOperationException("JWT SigningKey must be configured to generate tokens.");

        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(settings.TokenLifetimeMinutes);
        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.PlayerAudience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, playerAccountId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Name, username),
                new Claim(PlayerTokenClaims.AccountId, playerAccountId.ToString())
            ],
            notBefore: now,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
