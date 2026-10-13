using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GrindingThunder.Api.Application.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GrindingThunder.Api.Infrastructure.Security;

/// <summary>
/// Service that generates signed JWT bearer tokens using HMAC-SHA256 and configured settings.
/// </summary>
public class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTime ExpiresAt) GenerateToken(
        string username,
        Guid accountId,
        int securityStamp,
        IEnumerable<string> roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("A stable account ID is required to generate tokens.", nameof(accountId));
        }

        if (string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            throw new InvalidOperationException("JWT SigningKey must be configured to generate tokens.");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expiresAt = DateTime.UtcNow.AddMinutes(_options.TokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new(ClaimTypes.NameIdentifier, username),
            new(JwtRegisteredClaimNames.Sub, username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(AdminTokenClaims.AccountId, accountId.ToString()),
            new(AdminTokenClaims.SecurityStamp, securityStamp.ToString())
        };

        foreach (var role in roles)
        {
            if (!string.IsNullOrWhiteSpace(role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        var tokenDescriptor = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
        return (tokenString, expiresAt);
    }
}

