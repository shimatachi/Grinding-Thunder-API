using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class JwtTokenServiceTests
{
    private readonly JwtOptions _options = new()
    {
        Issuer = "GrindingThunder.Api.Test",
        Audience = "GrindingThunder.Admin.Test",
        SigningKey = "A_Very_Long_And_Secure_Secret_Key_For_Testing_Only_32_Chars!",
        TokenLifetimeMinutes = 30
    };

    [Fact]
    public void GenerateToken_ValidInputs_ReturnsValidJwtWithStandardAndRoleClaims()
    {
        var service = new JwtTokenService(Options.Create(_options));
        var roles = new[] { AuthRoles.Editor, AuthRoles.Publisher };

        var (token, expiresAt) = service.GenerateToken("testadmin", roles);

        Assert.NotNull(token);
        Assert.True(expiresAt > DateTime.UtcNow);

        // Validate token structure using JwtSecurityTokenHandler
        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.Name,
            ClockSkew = TimeSpan.Zero
        }, out var validatedToken);

        Assert.NotNull(principal);
        Assert.NotNull(validatedToken);
        Assert.Equal("testadmin", principal.Identity?.Name);
        Assert.True(principal.IsInRole(AuthRoles.Editor));
        Assert.True(principal.IsInRole(AuthRoles.Publisher));
        Assert.False(principal.IsInRole(AuthRoles.Admin));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GenerateToken_EmptyUsername_ThrowsArgumentException(string invalidUsername)
    {
        var service = new JwtTokenService(Options.Create(_options));
        Assert.ThrowsAny<ArgumentException>(() => service.GenerateToken(invalidUsername, [AuthRoles.Admin]));
    }

    [Fact]
    public void GenerateToken_MissingSigningKey_ThrowsInvalidOperationException()
    {
        var invalidOptions = new JwtOptions
        {
            SigningKey = ""
        };
        var service = new JwtTokenService(Options.Create(invalidOptions));

        Assert.Throws<InvalidOperationException>(() => service.GenerateToken("admin", [AuthRoles.Admin]));
    }
}

