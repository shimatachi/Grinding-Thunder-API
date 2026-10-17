using System.Text.Json.Serialization;
using GrindingThunder.Api.Domain.Services;
using GrindingThunder.Api.Infrastructure.Persistence;
using GrindingThunder.Api.Infrastructure.Security;
using GrindingThunder.Api.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // The host firewall and localhost-only published ports define the trusted proxy boundary.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddHealthChecks();

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
}

builder.Services.AddScoped<IResearchCalculatorService, ResearchCalculatorService>();
builder.Services.AddScoped<IVersionLifecycleService, VersionLifecycleService>();
builder.Services.AddScoped<ITechTreeImportService, TechTreeImportService>();

builder.Services.AddAdminSecurity(builder.Configuration, builder.Environment);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });
    
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(GetAllowedOrigins(builder.Configuration))
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

if (args.Contains("--migrate", StringComparer.OrdinalIgnoreCase))
{
    using var migrationScope = app.Services.CreateScope();
    var migrationDbContext = migrationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await migrationDbContext.Database.MigrateAsync();
    return;
}

if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.MigrateAsync();
            await DbInitializer.InitializeAsync(dbContext);
        }
    }

    app.MapOpenApi();
}

if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("GrindingThunder.AdminAccounts");

    // Explicit first-Admin bootstrap from external configuration/user secrets.
    // Idempotent: does nothing once any Admin-role account exists.
    await AdminAccountBootstrap.BootstrapAsync(dbContext, passwordHasher, app.Configuration, logger);

    // Deliberate Development-only sample accounts; never overwrites existing accounts.
    if (app.Environment.IsDevelopment())
    {
        await DevelopmentAdminAccountSeeder.SeedAsync(dbContext, passwordHasher);
    }
}

// app.UseHttpsRedirection();

app.UseForwardedHeaders();
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();

app.Run();

static string[] GetAllowedOrigins(IConfiguration configuration)
{
    var section = configuration.GetSection("Cors:AllowedOrigins");
    var origins = section.GetChildren()
        .Select(child => child.Value)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!.Trim().TrimEnd('/'))
        .ToArray();

    if (origins.Length == 0 && !string.IsNullOrWhiteSpace(section.Value))
    {
        origins = section.Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(origin => origin.TrimEnd('/'))
            .ToArray();
    }

    return origins.Length > 0
        ? origins
        : ["http://localhost:3000", "http://localhost:5035"];
}

public partial class Program { }

