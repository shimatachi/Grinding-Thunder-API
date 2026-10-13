# Run explicitly against the API's development PostgreSQL configuration.
# All migration and persistence checks use a temporary schema inside a transaction.
$ErrorActionPreference = 'Stop'
$apiDirectory = (Resolve-Path (Join-Path $PSScriptRoot '../../src/GrindingThunder.Api')).Path
$verificationDirectory = Join-Path $PSScriptRoot 'obj/batch21-22-postgres-verification'
New-Item -ItemType Directory -Path $verificationDirectory -Force | Out-Null
$testProject = [System.Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'GrindingThunder.Api.Tests.csproj'))
$projectText = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="$testProject" /></ItemGroup>
</Project>
"@
Set-Content -LiteralPath (Join-Path $verificationDirectory 'PostgresVerification.csproj') -Value $projectText
$programText = @'
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using GrindingThunder.Api.Infrastructure.Security;
using GrindingThunder.Api.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

var configuration = new ConfigurationBuilder().SetBasePath(args[0])
    .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
    .AddUserSecrets(typeof(ApplicationDbContext).Assembly, optional: true).AddEnvironmentVariables().Build();
var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseNpgsql(configuration.GetConnectionString("DefaultConnection")).Options;
await using var db = new ApplicationDbContext(options);
var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
Check(applied.Any(m => m.EndsWith("_AddAdminAccounts")), "Admin migration must be applied to development PostgreSQL first.");
Check(applied.Any(m => m.EndsWith("_AddPlayerAccounts")), "Player migration must be applied to development PostgreSQL first.");
Check(!(await db.Database.GetPendingMigrationsAsync()).Any(), "Development database has pending migrations.");
var before = await CatalogCounts(db);
var schema = "batch21_22_verify_" + Guid.NewGuid().ToString("N");
var allMigrations = db.Database.GetMigrations().ToList();
var baseline = allMigrations.Single(m => m.EndsWith("_RestrictResearchTreeVersionCascadeDelete"));
var adminMigration = allMigrations.Single(m => m.EndsWith("_AddAdminAccounts"));
var playerMigration = allMigrations.Single(m => m.EndsWith("_AddPlayerAccounts"));
var migrator = db.GetService<IMigrator>();
var adminUp = migrator.GenerateScript(baseline, adminMigration, MigrationsSqlGenerationOptions.NoTransactions);
var adminDown = migrator.GenerateScript(adminMigration, baseline, MigrationsSqlGenerationOptions.NoTransactions);
var playerUp = migrator.GenerateScript(adminMigration, playerMigration, MigrationsSqlGenerationOptions.NoTransactions);
var playerDown = migrator.GenerateScript(playerMigration, adminMigration, MigrationsSqlGenerationOptions.NoTransactions);

await using (var transaction = await db.Database.BeginTransactionAsync())
{
    // The generated migration uses unqualified tables. SET LOCAL keeps every
    // statement in our isolated schema, and rollback removes the schema itself.
    var createSchemaSql = "CREATE SCHEMA \"" + schema + "\"";
    var selectSchemaSql = "SET LOCAL search_path TO \"" + schema + "\"";
    await db.Database.ExecuteSqlRawAsync(createSchemaSql);
    await db.Database.ExecuteSqlRawAsync(selectSchemaSql);
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE "Vehicles" ("Id" uuid PRIMARY KEY, "Name" text NOT NULL, "ImageUrl" text NULL);
        CREATE TABLE "__EFMigrationsHistory" ("MigrationId" varchar(150) PRIMARY KEY, "ProductVersion" varchar(32) NOT NULL);
        """);
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"__EFMigrationsHistory\" VALUES ({baseline}, {"10.0.12"})");
    var vehicleId = Guid.NewGuid();
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Vehicles\" (\"Id\", \"Name\") VALUES ({vehicleId}, {"Preserved stable identity"})");

    // 1. AddAdminAccounts Up
    await db.Database.ExecuteSqlRawAsync(adminUp);
    Check((await db.Database.GetAppliedMigrationsAsync()).SequenceEqual(new[] { baseline, adminMigration }),
        "Admin Up migration did not update migration history.");

    // Exercise AdminAccounts behavior, PBKDF2 hash & login, and lifecycle rules
    var hasher = new PasswordHasher();
    var adminService = new AdminAccountService(db, hasher);
    var createRes = await adminService.CreateAsync("admin_pg", "AdminPass123!", [AuthRoles.Admin]);
    Check(createRes.Success, "Admin account creation failed.");
    var adminId = createRes.Account!.Id;

    var storedAdmin = await db.AdminAccounts.AsNoTracking().SingleAsync(a => a.Id == adminId);
    Check(hasher.VerifyPassword("AdminPass123!", storedAdmin.PasswordHash), "Stored PBKDF2 admin password hash is invalid.");

    var adminAuthTokens = new JwtTokenService(Options.Create(new JwtOptions { SigningKey = SecurityServiceExtensions.DevelopmentFallbackSigningKey }));
    var adminAuth = new AdminAuthService(db, hasher, adminAuthTokens);
    var loginRes = await adminAuth.AuthenticateAsync("ADMIN_PG", "AdminPass123!");
    Check(loginRes.Success && loginRes.Username == "admin_pg", "Admin login failed.");

    // Safe lifecycle: preserve at least one active Admin
    var deactRes = await adminService.UpdateAsync(adminId, "admin_pg", [AuthRoles.Admin], isActive: false);
    Check(!deactRes.Success && deactRes.ErrorMessage!.Contains("active Admin account"),
        "Deactivating the last Admin account succeeded.");

    var removeRoleRes = await adminService.UpdateAsync(adminId, "admin_pg", [AuthRoles.Editor], isActive: true);
    Check(!removeRoleRes.Success && removeRoleRes.ErrorMessage!.Contains("active Admin account"),
        "Removing the Admin role from the last Admin account succeeded.");

    var create2 = await adminService.CreateAsync("admin2", "Admin2Pass123!", [AuthRoles.Admin]);
    Check(create2.Success, "Creating second admin account failed.");
    var admin2Id = create2.Account!.Id;

    var deact1 = await adminService.UpdateAsync(adminId, "admin_pg", [AuthRoles.Admin], isActive: false);
    Check(deact1.Success, "Deactivating first admin failed when another active admin exists.");

    // Physical constraint checks for AdminAccounts and AdminAccountRoles
    // Uniqueness of NormalizedUsername
    db.AdminAccounts.Add(new AdminAccount
    {
        Id = Guid.NewGuid(),
        Username = "admin2_dup",
        NormalizedUsername = "ADMIN2",
        PasswordHash = "hash",
        IsActive = true,
        SecurityStamp = 0
    });
    await ExpectViolation(() => db.SaveChangesAsync(), "23505");
    db.ChangeTracker.Clear();

    // Check constraint: supported role values
    db.AdminAccountRoles.Add(new AdminAccountRole { AdminAccountId = admin2Id, Role = "SuperAdmin" });
    await ExpectViolation(() => db.SaveChangesAsync(), "23514");
    db.ChangeTracker.Clear();

    // Composite primary key (AdminAccountId, Role) uniqueness
    db.AdminAccountRoles.Add(new AdminAccountRole { AdminAccountId = admin2Id, Role = AuthRoles.Admin });
    await ExpectViolation(() => db.SaveChangesAsync(), "23505");
    db.ChangeTracker.Clear();

    // Foreign key to AdminAccount
    db.AdminAccountRoles.Add(new AdminAccountRole { AdminAccountId = Guid.NewGuid(), Role = AuthRoles.Editor });
    await ExpectViolation(() => db.SaveChangesAsync(), "23503");
    db.ChangeTracker.Clear();

    // Cascade delete of roles when AdminAccount is removed
    var toDelete = await db.AdminAccounts.Include(a => a.Roles).FirstAsync(a => a.Id == adminId);
    db.AdminAccounts.Remove(toDelete);
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    Check(!await db.AdminAccountRoles.AnyAsync(r => r.AdminAccountId == adminId),
        "Deleting AdminAccount did not cascade to AdminAccountRoles.");

    // 2. AddAdminAccounts Down
    await db.Database.ExecuteSqlRawAsync(adminDown);
    Check((await db.Database.GetAppliedMigrationsAsync()).SequenceEqual(new[] { baseline }),
        "Admin Down migration did not restore baseline history.");

    // 3. AddAdminAccounts Up again (Up/Down/Up)
    await db.Database.ExecuteSqlRawAsync(adminUp);
    Check((await db.Database.GetAppliedMigrationsAsync()).SequenceEqual(new[] { baseline, adminMigration }),
        "Reapplying Admin Up migration failed.");

    // 4. AddPlayerAccounts Up
    await db.Database.ExecuteSqlRawAsync(playerUp);
    Check((await db.Database.GetAppliedMigrationsAsync()).SequenceEqual(new[] { baseline, adminMigration, playerMigration }),
        "Player Up migration did not update migration history.");

    // Player registration, PBKDF2 hash, login, progress
    var playerTokens = new PlayerJwtTokenService(Options.Create(new JwtOptions { SigningKey = SecurityServiceExtensions.DevelopmentFallbackSigningKey }));
    var playerAuth = new PlayerAuthService(db, hasher, playerTokens);
    var registration = await playerAuth.RegisterAsync(new("postgres_player", "postgres-player@example.com", "VerificationPass123!"));
    Check(registration.Account is not null, "PostgreSQL registration failed.");
    var id = registration.Account!.PlayerAccountId;
    Check(hasher.VerifyPassword("VerificationPass123!", (await db.PlayerAccounts.AsNoTracking().SingleAsync()).PasswordHash), "Stored PBKDF2 hash is invalid.");
    Check((await playerAuth.LoginAsync(new("POSTGRES-PLAYER@EXAMPLE.COM", "VerificationPass123!")))?.PlayerAccountId == id, "PostgreSQL login failed.");
    Check((await playerAuth.RegisterAsync(new("POSTGRES_PLAYER", "other@example.com", "VerificationPass123!"))).Conflict, "Duplicate username was accepted.");
    Check((await playerAuth.RegisterAsync(new("other", "POSTGRES-PLAYER@EXAMPLE.COM", "VerificationPass123!"))).Conflict, "Duplicate email was accepted.");
    var preferences = new PlayerCalculatorPreferences(ResearchOptimizationMode.FewestMatches, 2500, 9000);
    Check((await new PlayerProgressService(db).UpdateAsync(id, new([vehicleId, vehicleId], preferences))).Progress is not null, "Progress save failed.");
    db.ChangeTracker.Clear();
    var progress = await new PlayerProgressService(db).GetAsync(id);
    Check(progress!.UnlockedVehicleIds.SequenceEqual(new[] { vehicleId }) && progress.Preferences == preferences, "Progress did not persist.");

    // Physical PostgreSQL constraints for PlayerAccounts
    db.PlayerAccounts.Add(new PlayerAccount { Id = Guid.NewGuid(), Username = "duplicate", NormalizedUsername = "POSTGRES_PLAYER", Email = "other@example.com", NormalizedEmail = "OTHER@EXAMPLE.COM", PasswordHash = "not-returned" });
    await ExpectViolation(() => db.SaveChangesAsync(), "23505");
    db.ChangeTracker.Clear();
    db.PlayerAccounts.Add(new PlayerAccount { Id = Guid.NewGuid(), Username = "duplicate", NormalizedUsername = "DUPLICATE", Email = "postgres-player@example.com", NormalizedEmail = "POSTGRES-PLAYER@EXAMPLE.COM", PasswordHash = "not-returned" });
    await ExpectViolation(() => db.SaveChangesAsync(), "23505");
    db.ChangeTracker.Clear();
    db.PlayerUnlockedVehicles.Add(new() { PlayerAccountId = id, VehicleId = Guid.NewGuid() });
    await ExpectViolation(() => db.SaveChangesAsync(), "23503");
    db.ChangeTracker.Clear();
    db.PlayerUnlockedVehicles.Add(new() { PlayerAccountId = Guid.NewGuid(), VehicleId = vehicleId });
    await ExpectViolation(() => db.SaveChangesAsync(), "23503");
    db.ChangeTracker.Clear();
    db.PlayerUnlockedVehicles.Add(new() { PlayerAccountId = id, VehicleId = vehicleId });
    await ExpectViolation(() => db.SaveChangesAsync(), "23505");
    db.ChangeTracker.Clear();
    var account = await db.PlayerAccounts.SingleAsync();
    account.AverageRpPerMatch = 0;
    await ExpectViolation(() => db.SaveChangesAsync(), "23514");
    db.ChangeTracker.Clear();
    account = await db.PlayerAccounts.SingleAsync();
    account.AverageNetSlPerMatch = -1;
    await ExpectViolation(() => db.SaveChangesAsync(), "23514");
    db.ChangeTracker.Clear();
    account = await db.PlayerAccounts.SingleAsync();
    account.OptimizationMode = (ResearchOptimizationMode)99;
    await ExpectViolation(() => db.SaveChangesAsync(), "23514");
    db.ChangeTracker.Clear();
    db.Vehicles.Remove(await db.Vehicles.SingleAsync());
    await ExpectViolation(() => db.SaveChangesAsync(), "23503");
    db.ChangeTracker.Clear();
    db.PlayerAccounts.Remove(await db.PlayerAccounts.SingleAsync());
    await db.SaveChangesAsync();
    Check(!await db.PlayerUnlockedVehicles.AnyAsync(), "Deleting a player did not cascade ownership.");
    db.ChangeTracker.Clear();

    // 5. AddPlayerAccounts Down
    await db.Database.ExecuteSqlRawAsync(playerDown);
    Check(await db.Vehicles.CountAsync() == 1, "Down migration changed stable vehicle data.");
    Check((await db.Database.GetAppliedMigrationsAsync()).SequenceEqual(new[] { baseline, adminMigration }),
        "Player Down migration did not restore admin history.");

    // 6. AddPlayerAccounts Up again (Up/Down/Up)
    await db.Database.ExecuteSqlRawAsync(playerUp);
    Check(await db.Vehicles.CountAsync() == 1 && !await db.PlayerAccounts.AnyAsync(),
        "Reapplying player migration failed.");

    await transaction.RollbackAsync();
}
db.ChangeTracker.Clear();
var after = await CatalogCounts(db);
Check(before.SequenceEqual(after), "Verification changed development catalog or administrator data.");
Check(applied.SequenceEqual(await db.Database.GetAppliedMigrationsAsync()), "Verification changed development migration history.");
Console.WriteLine("PostgreSQL verification passed: AddAdminAccounts & AddPlayerAccounts Up/Down/Up, PBKDF2 login, admin lifecycle, player progress, unique indexes, constraints, foreign keys, delete cascade/restriction. Temporary schema rolled back; catalog, admin data and migration history preserved.");

static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static async Task<int[]> CatalogCounts(ApplicationDbContext context) =>
    [
        await context.Vehicles.CountAsync(),
        await context.ResearchTreeVersions.CountAsync(),
        await context.VehicleTreeEntries.CountAsync(),
        await context.AdminAccounts.CountAsync(),
        await context.AdminAccountRoles.CountAsync(),
        await context.PlayerAccounts.CountAsync(),
        await context.PlayerUnlockedVehicles.CountAsync()
    ];
static async Task ExpectViolation(Func<Task> operation, string sqlState)
{
    try { await operation(); }
    catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == sqlState) { return; }
    throw new InvalidOperationException($"Expected PostgreSQL constraint violation {sqlState}.");
}
'@
Set-Content -LiteralPath (Join-Path $verificationDirectory 'Program.cs') -Value $programText
dotnet run --project (Join-Path $verificationDirectory 'PostgresVerification.csproj') -- $apiDirectory
if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL migration verification failed.' }
