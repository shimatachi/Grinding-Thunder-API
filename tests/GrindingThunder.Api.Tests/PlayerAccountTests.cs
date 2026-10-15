using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using GrindingThunder.Api.Infrastructure.Security;
using GrindingThunder.Api.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class PlayerAccountTests : IDisposable
{
    private readonly AdminAccountTestDatabase _database = new();
    private readonly PasswordHasher _hasher = new();
    private static readonly PlayerJwtTokenService Tokens = new(Options.Create(new JwtOptions
    {
        SigningKey = SecurityServiceExtensions.DevelopmentFallbackSigningKey
    }));

    private static ResearchTree NewTree() => new()
    {
        Id = Guid.NewGuid(),
        Nation = new Nation { Id = Guid.NewGuid(), Name = "Player test nation" },
        VehicleType = new VehicleType { Id = Guid.NewGuid(), Name = "Ground" }
    };

    public void Dispose() => _database.Dispose();
    private PlayerAuthService Auth(ApplicationDbContext db) => new(db, _hasher, Tokens);
    private static PlayerRegisterRequest Registration(string name = "player", string email = "player@example.com") =>
        new(name, email, "PlayerPass123!");

    [Fact]
    public async Task Register_PersistsSaltedPbkdf2Credentials_WithoutAdminAccount()
    {
        await using (var db = _database.CreateContext())
            Assert.NotNull((await Auth(db).RegisterAsync(Registration())).Account);
        await using var verification = _database.CreateContext();
        var account = await verification.PlayerAccounts.SingleAsync();
        Assert.Equal("PLAYER", account.NormalizedUsername);
        Assert.Equal("PLAYER@EXAMPLE.COM", account.NormalizedEmail);
        Assert.NotEqual("PlayerPass123!", account.PasswordHash);
        Assert.True(_hasher.VerifyPassword("PlayerPass123!", account.PasswordHash));
        Assert.NotEqual(account.PasswordHash, _hasher.HashPassword("PlayerPass123!"));
        Assert.Empty(await verification.AdminAccounts.ToListAsync());
    }

    [Theory]
    [InlineData("PLAYER", "another@example.com")]
    [InlineData("another", "PLAYER@EXAMPLE.COM")]
    public async Task Register_NormalizedUsernameOrEmailDuplicate_ReturnsConflict(string username, string email)
    {
        await using var db = _database.CreateContext();
        Assert.NotNull((await Auth(db).RegisterAsync(Registration())).Account);
        var duplicate = await Auth(db).RegisterAsync(Registration(username, email));
        Assert.True(duplicate.Conflict);
        Assert.Null(duplicate.Account);
        Assert.Equal(1, await db.PlayerAccounts.CountAsync());
    }

    [Theory]
    [InlineData("PLAYER", "different@example.com")]
    [InlineData("different", "PLAYER@EXAMPLE.COM")]
    public async Task Database_EnforcesBothUniqueIndexes(string username, string email)
    {
        await using var db = _database.CreateContext();
        await Auth(db).RegisterAsync(Registration());
        db.PlayerAccounts.Add(new PlayerAccount
        {
            Id = Guid.NewGuid(), Username = username, NormalizedUsername = username.ToUpperInvariant(),
            Email = email, NormalizedEmail = email.ToUpperInvariant(), PasswordHash = "not-exposed"
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("PLAYER")]
    [InlineData(" PLAYER@EXAMPLE.COM ")]
    public async Task Login_UsernameOrEmail_IsCaseInsensitive_AndRequiresCorrectPassword(string identifier)
    {
        await using (var db = _database.CreateContext())
            await Auth(db).RegisterAsync(Registration());
        await using var loginDb = _database.CreateContext();
        Assert.NotNull(await Auth(loginDb).LoginAsync(new(identifier, "PlayerPass123!")));
        Assert.Null(await Auth(loginDb).LoginAsync(new(identifier, "incorrect")));
        Assert.Null(await Auth(loginDb).LoginAsync(new("unknown", "PlayerPass123!")));
    }

    [Theory]
    [InlineData("", "player@example.com", "PlayerPass123!")]
    [InlineData("player@example.com", "player@example.com", "PlayerPass123!")]
    [InlineData("player", "invalid-email", "PlayerPass123!")]
    [InlineData("player", "player@example.com", "short")]
    [InlineData("player", "player@example.com", "        ")]
    public async Task Register_InvalidCredentials_DoesNotPersist(string username, string email, string password)
    {
        await using var db = _database.CreateContext();
        var result = await Auth(db).RegisterAsync(new(username, email, password));
        Assert.Null(result.Account);
        Assert.NotNull(result.Error);
        Assert.Empty(await db.PlayerAccounts.ToListAsync());
    }

    [Fact]
    public async Task Progress_DeduplicatesReplacesAndClearsStableIds_AndPersistsPreferences()
    {
        Guid playerId;
        var first = new Vehicle { Id = Guid.NewGuid(), ResearchTree = NewTree(), Name = "Stable identity without any version entry" };
        var second = new Vehicle { Id = Guid.NewGuid(), ResearchTree = NewTree(), Name = "Second identity" };
        var preferences = new PlayerCalculatorPreferences(ResearchOptimizationMode.FewestMatches, 2500, 8000);
        await using (var db = _database.CreateContext())
        {
            playerId = (await Auth(db).RegisterAsync(Registration())).Account!.PlayerAccountId;
            db.Vehicles.AddRange(first, second);
            await db.SaveChangesAsync();
            var result = await new PlayerProgressService(db).UpdateAsync(playerId, new([first.Id, first.Id, second.Id], preferences));
            Assert.NotNull(result.Progress);
            Assert.Equal(2, result.Progress.UnlockedVehicleIds.Count);
        }
        await using (var db = _database.CreateContext())
        {
            var service = new PlayerProgressService(db);
            var saved = await service.GetAsync(playerId);
            Assert.Equal(preferences, saved!.Preferences);
            Assert.Equal(new[] { first.Id, second.Id }.Order(), saved.UnlockedVehicleIds);
            var replaced = await service.UpdateAsync(playerId, new([second.Id], preferences));
            Assert.Equal(second.Id, Assert.Single(replaced.Progress!.UnlockedVehicleIds));
        }
        await using (var db = _database.CreateContext())
        {
            Assert.Equal(second.Id, Assert.Single((await new PlayerProgressService(db).GetAsync(playerId))!.UnlockedVehicleIds));
            Assert.NotNull((await new PlayerProgressService(db).UpdateAsync(playerId, new([], new()))).Progress);
        }
        await using var cleared = _database.CreateContext();
        var empty = await new PlayerProgressService(cleared).GetAsync(playerId);
        Assert.Empty(empty!.UnlockedVehicleIds);
        Assert.Equal(new PlayerCalculatorPreferences(), empty.Preferences);
    }

    [Fact]
    public async Task Progress_InvalidId_LeavesOwnershipAndPreferencesUnchanged()
    {
        await using var db = _database.CreateContext();
        var id = (await Auth(db).RegisterAsync(Registration())).Account!.PlayerAccountId;
        var vehicle = new Vehicle { Id = Guid.NewGuid(), ResearchTree = NewTree(), Name = "Known" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        var prefs = new PlayerCalculatorPreferences(ResearchOptimizationMode.LeastSl, 500, 5000);
        var service = new PlayerProgressService(db);
        await service.UpdateAsync(id, new([vehicle.Id], prefs));
        var failed = await service.UpdateAsync(id, new([Guid.NewGuid()], new(ResearchOptimizationMode.LeastRp, 1, 1)));
        Assert.Null(failed.Progress);
        var saved = await service.GetAsync(id);
        Assert.Equal(prefs, saved!.Preferences);
        Assert.Equal(vehicle.Id, Assert.Single(saved.UnlockedVehicleIds));
    }

    [Theory]
    [InlineData(-1, 100, 100)]
    [InlineData(4, 100, 100)]
    [InlineData(0, 0, 100)]
    [InlineData(0, 100, -1)]
    public async Task Database_EnforcesValidPreferences(int mode, int rp, int sl)
    {
        await using var db = _database.CreateContext();
        await Auth(db).RegisterAsync(Registration());
        var account = await db.PlayerAccounts.SingleAsync();
        account.OptimizationMode = (ResearchOptimizationMode)mode;
        account.AverageRpPerMatch = rp;
        account.AverageNetSlPerMatch = sl;
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_RejectsMissingVehicleAndDuplicateOwnership_AndRestrictsVehicleDeletion()
    {
        Guid id;
        var vehicle = new Vehicle { Id = Guid.NewGuid(), ResearchTree = NewTree(), Name = "Owned" };
        await using (var db = _database.CreateContext())
        {
            id = (await Auth(db).RegisterAsync(Registration())).Account!.PlayerAccountId;
            db.PlayerUnlockedVehicles.Add(new() { PlayerAccountId = id, VehicleId = Guid.NewGuid() });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = _database.CreateContext())
        {
            db.Vehicles.Add(vehicle);
            db.PlayerUnlockedVehicles.Add(new() { PlayerAccountId = id, VehicleId = vehicle.Id });
            await db.SaveChangesAsync();
        }
        await using (var db = _database.CreateContext())
        {
            db.PlayerUnlockedVehicles.Add(new() { PlayerAccountId = id, VehicleId = vehicle.Id });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = _database.CreateContext())
        {
            db.Vehicles.Remove(await db.Vehicles.SingleAsync());
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = _database.CreateContext())
        {
            db.PlayerAccounts.Remove(await db.PlayerAccounts.SingleAsync());
            await db.SaveChangesAsync();
            Assert.Empty(await db.PlayerUnlockedVehicles.ToListAsync());
            Assert.Equal(vehicle.Id, (await db.Vehicles.SingleAsync()).Id);
        }
    }

    [Fact]
    public async Task Progress_OverlappingSave_ReturnsConflict_AndRollsBackOwnershipDiff()
    {
        Guid id;
        var vehicle = new Vehicle { Id = Guid.NewGuid(), ResearchTree = NewTree(), Name = "Concurrency vehicle" };
        await using (var seed = _database.CreateContext())
        {
            id = (await Auth(seed).RegisterAsync(Registration())).Account!.PlayerAccountId;
            seed.Vehicles.Add(vehicle);
            await seed.SaveChangesAsync();
        }
        await using var stale = _database.CreateContext();
        // Hold the tracked old concurrency stamp, as if this request were paused while loading.
        await stale.PlayerAccounts.Include(a => a.UnlockedVehicles).SingleAsync();
        await using (var winner = _database.CreateContext())
            Assert.NotNull((await new PlayerProgressService(winner).UpdateAsync(id, new([], new(ResearchOptimizationMode.LeastSl, 10, 20)))).Progress);
        var conflict = await new PlayerProgressService(stale).UpdateAsync(id, new([vehicle.Id], new()));
        Assert.True(conflict.Conflict);
        await using var verification = _database.CreateContext();
        var saved = await new PlayerProgressService(verification).GetAsync(id);
        Assert.Empty(saved!.UnlockedVehicleIds);
        Assert.Equal(ResearchOptimizationMode.LeastSl, saved.Preferences.OptimizationMode);
    }
}
