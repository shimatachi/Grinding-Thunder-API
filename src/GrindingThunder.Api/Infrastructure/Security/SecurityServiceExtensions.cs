using System.Security.Claims;
using System.Text;
using GrindingThunder.Api.Application.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
            options.SigningKey = jwtOptions.SigningKey;
            options.TokenLifetimeMinutes = jwtOptions.TokenLifetimeMinutes;
        });

        // 2. Configure AdminAuthOptions
        var adminAuthOptions = configuration.GetSection(AdminAuthOptions.SectionName).Get<AdminAuthOptions>() ?? new AdminAuthOptions();

        // If no users configured in development/testing, register default accounts
        if (adminAuthOptions.Users.Count == 0 && (environment.IsDevelopment() || environment.IsEnvironment("Testing")))
        {
            var devHasher = new PasswordHasher();
            adminAuthOptions.Users =
            [
                new AdminUserConfig
                {
                    Username = "admin",
                    PasswordHash = devHasher.HashPassword("AdminPass123!"),
                    Roles = [AuthRoles.Admin]
                },
                new AdminUserConfig
                {
                    Username = "publisher",
                    PasswordHash = devHasher.HashPassword("PublisherPass123!"),
                    Roles = [AuthRoles.Publisher]
                },
                new AdminUserConfig
                {
                    Username = "editor",
                    PasswordHash = devHasher.HashPassword("EditorPass123!"),
                    Roles = [AuthRoles.Editor]
                }
            ];
        }

        services.Configure<AdminAuthOptions>(options =>
        {
            options.Users = adminAuthOptions.Users;
        });

        // 3. Register Security Services
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IAdminAuthService, AdminAuthService>();

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
                ValidAudience = jwtOptions.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                RoleClaimType = ClaimTypes.Role,
                NameClaimType = ClaimTypes.Name,
                ClockSkew = TimeSpan.Zero
            };
        });

        // 5. Configure Authorization Policies
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthPolicies.RequireAdminRole, policy =>
                policy.RequireRole(AuthRoles.Admin));

            options.AddPolicy(AuthPolicies.RequirePublisherRole, policy =>
                policy.RequireRole(AuthRoles.Publisher, AuthRoles.Admin));

            options.AddPolicy(AuthPolicies.RequireEditorRole, policy =>
                policy.RequireRole(AuthRoles.Editor, AuthRoles.Admin));
        });

        return services;
    }
}

