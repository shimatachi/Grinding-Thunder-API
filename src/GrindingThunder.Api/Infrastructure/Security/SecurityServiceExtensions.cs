using System.Security.Claims;
using System.Text;
using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Infrastructure.Persistence;
using GrindingThunder.Api.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace GrindingThunder.Api.Infrastructure.Security;

/// <summary>
/// Service collection extension methods for registering authentication, authorization, and administrative security services.
/// </summary>
public static class SecurityServiceExtensions
{
    public const string DevelopmentFallbackSigningKey = "DevelopmentSecretKey_WarThunderGrindingCalculator_Min32Bytes!";

    public static IServiceCollection AddAdminSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // 1. Configure and resolve JwtOptions
        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        var envSigningKey = configuration["JWT_SIGNING_KEY"];
        if (!string.IsNullOrWhiteSpace(envSigningKey))
        {
            jwtOptions.SigningKey = envSigningKey;
        }

        if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
        {
            if (environment.IsProduction())
            {
                throw new InvalidOperationException(
                    "JWT SigningKey must be configured in Production via 'Jwt:SigningKey' or 'JWT_SIGNING_KEY' environment variable.");
            }

            jwtOptions.SigningKey = DevelopmentFallbackSigningKey;
        }

        services.Configure<JwtOptions>(options =>
        {
            options.Issuer = jwtOptions.Issuer;
            options.Audience = jwtOptions.Audience;
            options.PlayerAudience = jwtOptions.PlayerAudience;
            options.SigningKey = jwtOptions.SigningKey;
            options.TokenLifetimeMinutes = jwtOptions.TokenLifetimeMinutes;
        });

        // 2. Register Security Services. Administrator credentials are no longer
        // read from configuration; authentication and account lifecycle management
        // operate exclusively on database-backed accounts.
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IAdminAuthService, AdminAuthService>();
        services.AddScoped<IAdminAccountService, AdminAccountService>();
        services.AddSingleton<PlayerJwtTokenService>();
        services.AddScoped<PlayerAuthService>();
        services.AddScoped<PlayerProgressService>();

        if (string.IsNullOrWhiteSpace(jwtOptions.PlayerAudience) || jwtOptions.PlayerAudience == jwtOptions.Audience)
            throw new InvalidOperationException("Player JWT audience must be nonempty and distinct from the administrator audience.");

        // 4. Configure Authentication (JWT Bearer)
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidAudiences = [jwtOptions.Audience, jwtOptions.PlayerAudience],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                RoleClaimType = ClaimTypes.Role,
                NameClaimType = ClaimTypes.Name,
                ClockSkew = TimeSpan.Zero
            };

            // Bind every token to a persistent account row: disabled accounts,
            // renamed accounts, and tokens issued before a credential/role change
            // (superseded security stamp) lose secured API access immediately.
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var principal = context.Principal;
                    var playerIdClaim = principal?.FindFirst(PlayerTokenClaims.AccountId)?.Value;
                    var isPlayerAudience = principal?.HasClaim("aud", jwtOptions.PlayerAudience) == true;
                    if (playerIdClaim is not null)
                    {
                        if (!isPlayerAudience || !Guid.TryParse(playerIdClaim, out var playerId) ||
                            principal!.HasClaim(c => c.Type == AdminTokenClaims.AccountId || c.Type == ClaimTypes.Role) ||
                            principal.HasClaim("aud", jwtOptions.Audience))
                        {
                            context.Fail("Player token has an invalid identity or audience.");
                            return;
                        }

                        var playerDb = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                        if (!await playerDb.PlayerAccounts.AsNoTracking().AnyAsync(a => a.Id == playerId, context.HttpContext.RequestAborted))
                            context.Fail("Player account no longer exists.");
                        return;
                    }
                    if (isPlayerAudience)
                    {
                        context.Fail("Player audience requires a persistent player identity.");
                        return;
                    }
                    var accountIdClaim = principal?.FindFirst(AdminTokenClaims.AccountId)?.Value;
                    var stampClaim = principal?.FindFirst(AdminTokenClaims.SecurityStamp)?.Value;

                    if (!Guid.TryParse(accountIdClaim, out var accountId) ||
                        !int.TryParse(stampClaim, out var securityStamp))
                    {
                        context.Fail("Token is not bound to a persistent administrator account.");
                        return;
                    }

                    var dbContext = context.HttpContext.RequestServices
                        .GetRequiredService<ApplicationDbContext>();

                    var state = await dbContext.AdminAccounts
                        .AsNoTracking()
                        .Where(a => a.Id == accountId)
                        .Select(a => new { a.IsActive, a.SecurityStamp })
                        .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

                    if (state == null || !state.IsActive || state.SecurityStamp != securityStamp)
                    {
                        context.Fail("Token was superseded by an account change.");
                    }
                }
            };
        });

        // 5. Configure Authorization Policies
        services.AddAuthorization(options =>
        {
            // Existing bare [Authorize] endpoints describe administrator sessions.
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser().RequireClaim(AdminTokenClaims.AccountId).Build();
            options.AddPolicy(AuthPolicies.RequirePlayer, policy =>
                policy.RequireAuthenticatedUser().RequireClaim(PlayerTokenClaims.AccountId));
            options.AddPolicy(AuthPolicies.RequireAdminRole, policy =>
                policy.RequireClaim(AdminTokenClaims.AccountId).RequireRole(AuthRoles.Admin));

            options.AddPolicy(AuthPolicies.RequirePublisherRole, policy =>
                policy.RequireClaim(AdminTokenClaims.AccountId).RequireRole(AuthRoles.Publisher, AuthRoles.Admin));

            options.AddPolicy(AuthPolicies.RequireEditorRole, policy =>
                policy.RequireClaim(AdminTokenClaims.AccountId).RequireRole(AuthRoles.Editor, AuthRoles.Admin));
        });

        return services;
    }
}

