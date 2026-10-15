using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Infrastructure.Persistence;
using GrindingThunder.Api.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace GrindingThunder.Api.Tests;

// Explicit PostgreSQL verification entry point used by Verify-VehicleOwnershipPostgres.ps1.
// Every fixture and migration runs in an isolated schema inside a rolled-back transaction.
public static class VehicleOwnershipPostgresVerification
{
    private const string Baseline = "20261013145650_AddPlayerAccounts";
    private const string OwnershipMigration = "20261015142100_ScopeVehiclesToResearchTree";

    public static async Task RunAsync(string apiDirectory)
    {
        var configuration = new ConfigurationBuilder().SetBasePath(apiDirectory)
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets(typeof(ApplicationDbContext).Assembly, optional: true).AddEnvironmentVariables().Build();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(configuration.GetConnectionString("DefaultConnection")).Options);
        await db.Database.OpenConnectionAsync();

        // Read the real database before testing fixtures. Report exact blockers; never guess a mapping.
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        if (!applied.Contains(OwnershipMigration))
        {
            var blockers = await db.Database.SqlQueryRaw<string>("""
                SELECT format('Vehicle %s "%s": candidate trees [%s]', v."Id", v."Name",
                    coalesce(string_agg(DISTINCT r."ResearchTreeId"::text, ', '), 'none')) AS "Value"
                FROM "Vehicles" v
                LEFT JOIN "VehicleTreeEntries" e ON e."VehicleId" = v."Id"
                LEFT JOIN "ResearchTreeVersions" r ON r."Id" = e."ResearchTreeVersionId"
                GROUP BY v."Id", v."Name"
                HAVING count(DISTINCT r."ResearchTreeId") <> 1
                ORDER BY v."Id"
                """).ToListAsync();
            var duplicates = await db.Database.SqlQueryRaw<string>("""
                SELECT format('Duplicate tree=%s name="%s": vehicle IDs [%s]', ownership.tree_id,
                    lower(trim(v."Name")), string_agg(v."Id"::text, ', ')) AS "Value"
                FROM "Vehicles" v
                JOIN (
                    SELECT e."VehicleId", min(r."ResearchTreeId"::text) AS tree_id
                    FROM "VehicleTreeEntries" e
                    JOIN "ResearchTreeVersions" r ON r."Id" = e."ResearchTreeVersionId"
                    GROUP BY e."VehicleId" HAVING count(DISTINCT r."ResearchTreeId") = 1
                ) ownership ON ownership."VehicleId" = v."Id"
                GROUP BY ownership.tree_id, lower(trim(v."Name")) HAVING count(*) > 1
                """).ToListAsync();
            if (blockers.Count + duplicates.Count > 0)
                throw new InvalidOperationException("Existing-data migration blocked. No changes made:\n" + string.Join("\n", blockers.Concat(duplicates)));
            Console.WriteLine("Existing-data ownership audit passed; no unowned, multi-tree, or duplicate-name blockers.");
        }

        var migrator = db.GetService<IMigrator>();
        var migrations = db.GetService<IMigrationsAssembly>();
        var sqlGenerator = db.GetService<IMigrationsSqlGenerator>();
        var up = migrator.GenerateScript(Baseline, OwnershipMigration, MigrationsSqlGenerationOptions.NoTransactions);
        var down = migrator.GenerateScript(OwnershipMigration, Baseline, MigrationsSqlGenerationOptions.NoTransactions);
        foreach (var scenario in new[] { "safe", "unowned", "ambiguous", "duplicate" })
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var schema = "batch23_verify_" + Guid.NewGuid().ToString("N");
            // SQL identifiers cannot be parameters; this identifier contains only a fixed prefix and GUID hex.
            var createSchemaSql = "CREATE SCHEMA \"" + schema + "\"";
            var selectSchemaSql = "SET LOCAL search_path TO \"" + schema + "\"";
            await db.Database.ExecuteSqlRawAsync(createSchemaSql);
            await db.Database.ExecuteSqlRawAsync(selectSchemaSql);
            // Some applied legacy raw-SQL operations omit trailing semicolons.
            // Execute individual commands, as EF's migrator does, instead of concatenating them.
            foreach (var (id, type) in migrations.Migrations.Where(m => string.CompareOrdinal(m.Key, Baseline) <= 0))
            {
                var migration = migrations.CreateMigration(type, db.Database.ProviderName!);
                var targetModel = db.GetService<IModelRuntimeInitializer>().Initialize(migration.TargetModel, designTime: true);
                foreach (var command in sqlGenerator.Generate(migration.UpOperations, targetModel))
                    await db.Database.ExecuteSqlRawAsync(command.CommandText);
            }
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE "__EFMigrationsHistory" ("MigrationId" varchar(150) PRIMARY KEY, "ProductVersion" varchar(32) NOT NULL);
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"__EFMigrationsHistory\" VALUES ({Baseline}, {"10.0.12"})");
            // UUID suffixes make relationships readable and deterministic inside this private schema.
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Nations" ("Id", "Name") VALUES ('00000000-0000-0000-0000-000000000001', 'Test nation');
                INSERT INTO "VehicleTypes" ("Id", "Name") VALUES
                    ('00000000-0000-0000-0000-000000000002', 'Fixture Ground'),
                    ('00000000-0000-0000-0000-000000000003', 'Fixture Aviation');
                INSERT INTO "ResearchTrees" ("Id", "NationId", "VehicleTypeId") VALUES
                    ('00000000-0000-0000-0000-000000000010', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000002'),
                    ('00000000-0000-0000-0000-000000000011', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000003');
                INSERT INTO "GameUpdates" ("Id", "Version") VALUES
                    ('00000000-0000-0000-0000-000000000020', 'test-a'), ('00000000-0000-0000-0000-000000000021', 'test-b');
                INSERT INTO "ResearchTreeVersions" ("Id", "ResearchTreeId", "GameUpdateId", "Status") VALUES
                    ('00000000-0000-0000-0000-000000000030', '00000000-0000-0000-0000-000000000010', '00000000-0000-0000-0000-000000000020', 1),
                    ('00000000-0000-0000-0000-000000000031', '00000000-0000-0000-0000-000000000010', '00000000-0000-0000-0000-000000000021', 0),
                    ('00000000-0000-0000-0000-000000000032', '00000000-0000-0000-0000-000000000011', '00000000-0000-0000-0000-000000000020', 0);
                INSERT INTO "TreeRanks" ("Id", "ResearchTreeVersionId", "RankNumber", "RequiredVehiclesUnlocked") VALUES
                    ('00000000-0000-0000-0000-000000000040', '00000000-0000-0000-0000-000000000030', 1, 0),
                    ('00000000-0000-0000-0000-000000000041', '00000000-0000-0000-0000-000000000031', 1, 0),
                    ('00000000-0000-0000-0000-000000000042', '00000000-0000-0000-0000-000000000032', 1, 0);
                INSERT INTO "Vehicles" ("Id", "Name") VALUES
                    ('00000000-0000-0000-0000-000000000050', 'Shared'), ('00000000-0000-0000-0000-000000000051', 'SHARED');
                INSERT INTO "VehicleTreeEntries" ("Id", "ResearchTreeVersionId", "VehicleId", "TreeRankId", "RpCost", "SlCost", "TreeColumn", "TreeRow") VALUES
                    ('00000000-0000-0000-0000-000000000060', '00000000-0000-0000-0000-000000000030', '00000000-0000-0000-0000-000000000050', '00000000-0000-0000-0000-000000000040', 100, 200, 1, 1),
                    ('00000000-0000-0000-0000-000000000061', '00000000-0000-0000-0000-000000000031', '00000000-0000-0000-0000-000000000050', '00000000-0000-0000-0000-000000000041', 400, 600, 1, 1),
                    ('00000000-0000-0000-0000-000000000062', '00000000-0000-0000-0000-000000000032', '00000000-0000-0000-0000-000000000051', '00000000-0000-0000-0000-000000000042', 900, 1000, 1, 1);
                INSERT INTO "PlayerAccounts" ("Id", "Username", "NormalizedUsername", "Email", "NormalizedEmail", "PasswordHash", "OptimizationMode", "ProgressStamp") VALUES
                    ('00000000-0000-0000-0000-000000000070', 'test', 'TEST', 'test@example.com', 'TEST@EXAMPLE.COM', 'unused', 0, '00000000-0000-0000-0000-000000000071');
                INSERT INTO "PlayerUnlockedVehicles" ("PlayerAccountId", "VehicleId") VALUES
                    ('00000000-0000-0000-0000-000000000070', '00000000-0000-0000-0000-000000000050');
                """);
            if (scenario == "unowned")
                await db.Database.ExecuteSqlRawAsync("""INSERT INTO "Vehicles" ("Id", "Name") VALUES ('00000000-0000-0000-0000-000000000099', 'Unowned')""");
            if (scenario == "ambiguous")
                await db.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "VehicleTreeEntries" ("Id", "ResearchTreeVersionId", "VehicleId", "TreeRankId", "RpCost", "SlCost", "TreeColumn", "TreeRow") VALUES
                        ('00000000-0000-0000-0000-000000000096', '00000000-0000-0000-0000-000000000032', '00000000-0000-0000-0000-000000000050', '00000000-0000-0000-0000-000000000042', 1, 1, 2, 1);
                    """);
            if (scenario == "duplicate")
            {
                await db.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "Vehicles" ("Id", "Name") VALUES ('00000000-0000-0000-0000-000000000098', ' shared ');
                    INSERT INTO "VehicleTreeEntries" ("Id", "ResearchTreeVersionId", "VehicleId", "TreeRankId", "RpCost", "SlCost", "TreeColumn", "TreeRow") VALUES
                        ('00000000-0000-0000-0000-000000000097', '00000000-0000-0000-0000-000000000030', '00000000-0000-0000-0000-000000000098', '00000000-0000-0000-0000-000000000040', 1, 1, 2, 1);
                    """);
            }

            if (scenario != "safe")
            {
                await transaction.CreateSavepointAsync("migration_attempt");
                var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(up));
                Assert.Equal("P0001", ex.SqlState);
                Assert.Contains(scenario == "duplicate" ? "duplicate normalized name" : "unowned or multi-tree", ex.MessageText);
                await transaction.RollbackToSavepointAsync("migration_attempt");
                Assert.DoesNotContain(OwnershipMigration, await db.Database.GetAppliedMigrationsAsync());
                Assert.Equal(1, await db.PlayerUnlockedVehicles.CountAsync());
            }
            else
            {
                await db.Database.ExecuteSqlRawAsync(up);
                var stableId = Guid.Parse("00000000-0000-0000-0000-000000000050");
                var ownerId = Guid.Parse("00000000-0000-0000-0000-000000000010");
                Assert.Equal(ownerId, (await db.Vehicles.AsNoTracking().SingleAsync(v => v.Id == stableId)).ResearchTreeId);
                Assert.Equal(stableId, (await db.PlayerUnlockedVehicles.AsNoTracking().SingleAsync()).VehicleId);
                Assert.Equal(3, await db.VehicleTreeEntries.CountAsync());
                foreach (var versionId in new[] { "00000000-0000-0000-0000-000000000030", "00000000-0000-0000-0000-000000000031" })
                {
                    var result = await new ResearchCalculatorService(db).CalculateResearchAsync(new(Guid.Parse(versionId), stableId, [], [], 100, 100));
                    Assert.Equal(versionId.EndsWith("30") ? 100 : 400, result.TotalRpRequired);
                }
                var playerId = Guid.Parse("00000000-0000-0000-0000-000000000070");
                Assert.NotNull((await new PlayerProgressService(db).UpdateAsync(playerId, new([stableId], new()))).Progress);
                db.ChangeTracker.Clear();

                await Violation("""UPDATE "VehicleTreeEntries" SET "VehicleId" = '00000000-0000-0000-0000-000000000051' WHERE "Id" = '00000000-0000-0000-0000-000000000061'""", "23503");
                await Violation("""UPDATE "VehicleTreeEntries" SET "ResearchTreeId" = '00000000-0000-0000-0000-000000000011' WHERE "Id" = '00000000-0000-0000-0000-000000000061'""", "23503");
                await Violation("""UPDATE "Vehicles" SET "ResearchTreeId" = NULL WHERE "Id" = '00000000-0000-0000-0000-000000000050'""", "23502");
                await Violation("""INSERT INTO "Vehicles" ("Id", "ResearchTreeId", "Name") VALUES (gen_random_uuid(), '00000000-0000-0000-0000-000000000010', ' shared ')""", "23505");
                await Violation("""DELETE FROM "Vehicles" WHERE "Id" = '00000000-0000-0000-0000-000000000050'""", "23503");
                await Violation("""DELETE FROM "ResearchTrees" WHERE "Id" = '00000000-0000-0000-0000-000000000010'""", "23503");
                await db.Database.ExecuteSqlRawAsync(down);
                Assert.Equal(stableId, (await db.PlayerUnlockedVehicles.AsNoTracking().SingleAsync()).VehicleId);
                await db.Database.ExecuteSqlRawAsync(up);
                Assert.Equal(ownerId, (await db.Vehicles.AsNoTracking().SingleAsync(v => v.Id == stableId)).ResearchTreeId);
                Assert.Equal(stableId, (await db.PlayerUnlockedVehicles.AsNoTracking().SingleAsync()).VehicleId);

                // Entryless identities created after Up cannot be downgraded without losing their mapping.
                await db.Database.ExecuteSqlRawAsync("""INSERT INTO "Vehicles" ("Id", "ResearchTreeId", "Name") VALUES (gen_random_uuid(), '00000000-0000-0000-0000-000000000010', 'Entryless')""");
                await transaction.CreateSavepointAsync("down_attempt");
                var downError = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(down));
                Assert.Contains("entryless vehicles", downError.MessageText);
                await transaction.RollbackToSavepointAsync("down_attempt");

                async Task Violation(string sql, string state)
                {
                    await transaction.CreateSavepointAsync("constraint_attempt");
                    var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
                    Assert.Equal(state, ex.SqlState);
                    await transaction.RollbackToSavepointAsync("constraint_attempt");
                }
            }
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            Console.WriteLine($"PostgreSQL scenario passed: {scenario}; private schema rolled back.");
        }
        Assert.Equal(applied, await db.Database.GetAppliedMigrationsAsync());
        Console.WriteLine("PostgreSQL verification passed: ownership backfill, guards, Up/Down/Up, FKs, uniqueness, delete restrictions, stable player IDs and version-scoped calculator. Existing database unchanged.");
    }
}
