using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using GrindingThunder.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class PlayerIntegrationTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();
    public Task InitializeAsync() => factory.InitializeDatabaseAsync();
    public Task DisposeAsync() { _client.Dispose(); return Task.CompletedTask; }

    private async Task<PlayerTokenResponse> RegisterAsync(string? username = null, string? email = null)
    {
        var name = username ?? $"player_{Guid.NewGuid():N}";
        var response = await _client.PostAsJsonAsync("/api/player/auth/register",
            new PlayerRegisterRequest(name, email ?? $"{name}@example.com", "PlayerPass123!"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", raw, StringComparison.OrdinalIgnoreCase);
        var account = await response.Content.ReadFromJsonAsync<PlayerTokenResponse>();
        Assert.NotNull(account);
        return account;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Register_LoginByUsernameAndEmail_UsesSeparateAudienceAndNoAdminClaims()
    {
        var account = await RegisterAsync();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(account.Token);
        var settings = factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        Assert.Equal(settings.PlayerAudience, Assert.Single(jwt.Audiences));
        Assert.Equal(account.PlayerAccountId.ToString(), jwt.Subject);
        Assert.Equal(account.PlayerAccountId.ToString(), jwt.Claims.Single(c => c.Type == PlayerTokenClaims.AccountId).Value);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == AdminTokenClaims.AccountId || c.Type == ClaimTypes.Role || c.Type == "role");
        Assert.Equal("Bearer", account.TokenType);
        Assert.True(account.ExpiresAt > DateTime.UtcNow);

        foreach (var identifier in new[] { account.Username.ToUpperInvariant(), account.Email.ToUpperInvariant() })
        {
            var login = await _client.PostAsJsonAsync("/api/player/auth/login", new PlayerLoginRequest(identifier, "PlayerPass123!"));
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.Equal(account.PlayerAccountId, (await login.Content.ReadFromJsonAsync<PlayerTokenResponse>())!.PlayerAccountId);
        }
    }

    [Fact]
    public async Task Register_DuplicateUsernameOrEmail_Returns409()
    {
        var account = await RegisterAsync();
        var duplicateName = await _client.PostAsJsonAsync("/api/player/auth/register",
            new PlayerRegisterRequest(account.Username.ToUpperInvariant(), $"{Guid.NewGuid():N}@example.com", "PlayerPass123!"));
        var duplicateEmail = await _client.PostAsJsonAsync("/api/player/auth/register",
            new PlayerRegisterRequest($"other_{Guid.NewGuid():N}", account.Email.ToUpperInvariant(), "PlayerPass123!"));
        Assert.Equal(HttpStatusCode.Conflict, duplicateName.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicateEmail.StatusCode);
    }

    [Theory]
    [InlineData("", "p@example.com", "PlayerPass123!")]
    [InlineData("player", "invalid", "PlayerPass123!")]
    [InlineData("player", "p@example.com", "short")]
    [InlineData("admin@example.com", "p@example.com", "PlayerPass123!")]
    public async Task Register_InvalidInput_Returns400(string username, string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/player/auth/register", new PlayerRegisterRequest(username, email, password));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPasswordAndUnknownAccount_ShareGeneric401()
    {
        var account = await RegisterAsync();
        var wrong = await _client.PostAsJsonAsync("/api/player/auth/login", new PlayerLoginRequest(account.Username, "incorrect"));
        var unknown = await _client.PostAsJsonAsync("/api/player/auth/login", new PlayerLoginRequest("missing", "incorrect"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Me_ValidPlayerToken_ReturnsProfile_AnonymousReturns401_AdminReturns403()
    {
        var account = await RegisterAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/player/auth/me", account.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<PlayerProfileResponse>();
        Assert.NotNull(profile);
        Assert.Equal(account.PlayerAccountId, profile.PlayerAccountId);
        Assert.Equal(account.Username, profile.Username);
        Assert.Equal(account.Email, profile.Email);

        var anon = await _client.GetAsync("/api/player/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);

        var adminToken = await factory.GetTokenAsync("admin", "AdminPass123!");
        var admin = await SendAsync(HttpMethod.Get, "/api/player/auth/me", adminToken);
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
    }

    [Fact]
    public async Task Progress_SaveReloadReplaceClear_PersistsAcrossLoginSessions_AndIsolatesPlayers()
    {
        var player = await RegisterAsync();
        var other = await RegisterAsync();
        var initial = await (await SendAsync(HttpMethod.Get, "/api/player/progress", player.Token)).Content.ReadFromJsonAsync<PlayerProgressResponse>();
        Assert.Empty(initial!.UnlockedVehicleIds);
        Assert.Equal(new PlayerCalculatorPreferences(), initial.Preferences);

        List<Guid> vehicles;
        using (var scope = factory.Services.CreateScope())
            vehicles = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Vehicles.OrderBy(v => v.Id).Select(v => v.Id).Take(2).ToListAsync();
        Assert.Equal(2, vehicles.Count);
        var preferences = new PlayerCalculatorPreferences(ResearchOptimizationMode.FewestMatches, 3500, 12000);
        var put = await SendAsync(HttpMethod.Put, "/api/player/progress", player.Token,
            new UpdatePlayerProgressRequest([vehicles[0], vehicles[0], vehicles[1]], preferences));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var saved = await put.Content.ReadFromJsonAsync<PlayerProgressResponse>();
        Assert.Equal(vehicles.Order(), saved!.UnlockedVehicleIds);

        var login = await _client.PostAsJsonAsync("/api/player/auth/login", new PlayerLoginRequest(player.Email, "PlayerPass123!"));
        var newToken = (await login.Content.ReadFromJsonAsync<PlayerTokenResponse>())!.Token;
        var reload = await (await SendAsync(HttpMethod.Get, "/api/player/progress", newToken)).Content.ReadFromJsonAsync<PlayerProgressResponse>();
        Assert.Equal(preferences, reload!.Preferences);
        Assert.Equal(vehicles.Order(), reload.UnlockedVehicleIds);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, "/api/player/progress", newToken,
            new UpdatePlayerProgressRequest([vehicles[1]], preferences))).StatusCode);
        var replaced = await (await SendAsync(HttpMethod.Get, "/api/player/progress", player.Token)).Content.ReadFromJsonAsync<PlayerProgressResponse>();
        Assert.Equal(vehicles[1], Assert.Single(replaced!.UnlockedVehicleIds));

        var isolated = await (await SendAsync(HttpMethod.Get, "/api/player/progress", other.Token)).Content.ReadFromJsonAsync<PlayerProgressResponse>();
        Assert.Empty(isolated!.UnlockedVehicleIds);
        Assert.Equal(new PlayerCalculatorPreferences(), isolated.Preferences);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, "/api/player/progress", newToken,
            new UpdatePlayerProgressRequest([], new()))).StatusCode);
        var cleared = await (await SendAsync(HttpMethod.Get, "/api/player/progress", newToken)).Content.ReadFromJsonAsync<PlayerProgressResponse>();
        Assert.Empty(cleared!.UnlockedVehicleIds);
        Assert.Equal(new PlayerCalculatorPreferences(), cleared.Preferences);
    }

    [Theory]
    [InlineData(0, 0, 100)]
    [InlineData(0, 100, -10)]
    [InlineData(99, 100, 100)]
    public async Task Progress_InvalidPreferences_Return400(int mode, int rp, int sl)
    {
        var player = await RegisterAsync();
        var response = await SendAsync(HttpMethod.Put, "/api/player/progress", player.Token,
            new { unlockedVehicleIds = Array.Empty<Guid>(), preferences = new { optimizationMode = mode, averageRpPerMatch = rp, averageNetSlPerMatch = sl } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Progress_UnknownStableIdOrVersionEntryId_Return400_AndDoesNotSavePreferences()
    {
        var player = await RegisterAsync();
        Guid entryId;
        using (var scope = factory.Services.CreateScope())
            entryId = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().VehicleTreeEntries.OrderBy(e => e.Id).Select(e => e.Id).FirstAsync();
        foreach (var id in new[] { Guid.NewGuid(), Guid.Empty, entryId })
        {
            var response = await SendAsync(HttpMethod.Put, "/api/player/progress", player.Token,
                new UpdatePlayerProgressRequest([id], new(ResearchOptimizationMode.LeastSl, 1, 1)));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        var saved = await (await SendAsync(HttpMethod.Get, "/api/player/progress", player.Token)).Content.ReadFromJsonAsync<PlayerProgressResponse>();
        Assert.Empty(saved!.UnlockedVehicleIds);
        Assert.Equal(new PlayerCalculatorPreferences(), saved.Preferences);
    }

    [Fact]
    public async Task Progress_MissingRequiredFields_Return400()
    {
        var player = await RegisterAsync();
        foreach (var body in new object[] { new { preferences = new { } }, new { unlockedVehicleIds = Array.Empty<Guid>() }, new { unlockedVehicleIds = (object?)null, preferences = new { } } })
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Put, "/api/player/progress", player.Token, body)).StatusCode);
    }

    [Fact]
    public async Task Progress_AnonymousReturns401_AdminReturns403()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/player/progress")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PutAsJsonAsync("/api/player/progress", new UpdatePlayerProgressRequest([], new()))).StatusCode);
        var admin = await factory.GetTokenAsync("admin", "AdminPass123!");
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/api/player/progress", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Put, "/api/player/progress", admin, new UpdatePlayerProgressRequest([], new()))).StatusCode);
    }

    [Fact]
    public async Task PlayerToken_Returns403_OnEveryRegisteredAdminRouteAndMethod()
    {
        var player = await RegisterAsync();
        var source = factory.Services.GetRequiredService<EndpointDataSource>();
        var endpoints = source.Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.TrimStart('/').StartsWith("api/admin/", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(endpoints);
        var checkedMethods = 0;
        foreach (var endpoint in endpoints)
        {
            var route = "/" + Regex.Replace(endpoint.RoutePattern.RawText!.TrimStart('/'), @"\{[^}]+\}", Guid.NewGuid().ToString());
            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), route);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", player.Token);
                request.Content = route.Contains("/import", StringComparison.Ordinal)
                    ? new MultipartFormDataContent()
                    : JsonContent.Create(new { });
                var response = await _client.SendAsync(request);
                Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
                    $"{method} {route}: expected 403, got {(int)response.StatusCode}.");
                checkedMethods++;
            }
        }
        Assert.True(checkedMethods > 50);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/api/auth/me", player.Token)).StatusCode);
    }

    [Fact]
    public async Task PlayerAndAdmin_CredentialsAreIndependent_EvenWithSameUsername()
    {
        var player = await RegisterAsync("admin", "player-admin@example.com");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(player.Username, "PlayerPass123!"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/player/auth/login", new PlayerLoginRequest("admin", "AdminPass123!"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", "AdminPass123!"))).StatusCode);
    }

    [Fact]
    public async Task Catalog_DeleteOwnedVehicle_Returns409_UntilPlayerClearsOwnership()
    {
        var player = await RegisterAsync();
        var vehicle = new Vehicle { Id = Guid.NewGuid(), Name = $"Owned catalog vehicle {Guid.NewGuid():N}" };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            vehicle.ResearchTreeId = (await db.ResearchTrees.FirstAsync()).Id;
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, "/api/player/progress", player.Token,
            new UpdatePlayerProgressRequest([vehicle.Id], new()))).StatusCode);
        var admin = await factory.GetTokenAsync("admin", "AdminPass123!");
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Delete, $"/api/admin/vehicles/{vehicle.Id}", player.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(HttpMethod.Delete, $"/api/admin/vehicles/{vehicle.Id}", admin)).StatusCode);
        var saved = await (await SendAsync(HttpMethod.Get, "/api/player/progress", player.Token)).Content.ReadFromJsonAsync<PlayerProgressResponse>();
        Assert.Equal(vehicle.Id, Assert.Single(saved!.UnlockedVehicleIds));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, "/api/player/progress", player.Token,
            new UpdatePlayerProgressRequest([], new()))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, $"/api/admin/vehicles/{vehicle.Id}", admin)).StatusCode);
    }

    [Fact]
    public async Task Token_DeletedPlayer_Returns401()
    {
        var player = await RegisterAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.PlayerAccounts.Remove(await db.PlayerAccounts.SingleAsync(a => a.Id == player.PlayerAccountId));
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/player/progress", player.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/player/auth/me", player.Token)).StatusCode);
    }

    [Fact]
    public async Task Token_MixedIdentitiesRolesOrWrongAudience_Returns401()
    {
        var player = await RegisterAsync();
        var settings = factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        var variants = new (string Audience, Claim[] Claims)[]
        {
            (settings.Audience, [new(PlayerTokenClaims.AccountId, player.PlayerAccountId.ToString())]),
            (settings.PlayerAudience, [new(PlayerTokenClaims.AccountId, player.PlayerAccountId.ToString()), new(ClaimTypes.Role, AuthRoles.Admin)]),
            (settings.PlayerAudience, [new(PlayerTokenClaims.AccountId, player.PlayerAccountId.ToString()), new(AdminTokenClaims.AccountId, player.PlayerAccountId.ToString())]),
            (settings.PlayerAudience, [new(AdminTokenClaims.AccountId, player.PlayerAccountId.ToString()), new(AdminTokenClaims.SecurityStamp, "0"), new(ClaimTypes.Role, AuthRoles.Admin)]),
            (settings.PlayerAudience, [new(PlayerTokenClaims.AccountId, "invalid-guid")])
        };
        foreach (var variant in variants)
        {
            var jwt = new JwtSecurityToken(settings.Issuer, variant.Audience, variant.Claims,
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5),
                new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)), SecurityAlgorithms.HmacSha256));
            var token = new JwtSecurityTokenHandler().WriteToken(jwt);
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/player/progress", token)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/admin/accounts", token)).StatusCode);
        }
    }

    [Fact]
    public async Task AnonymousCalculator_RemainsIndependentOfSavedPlayerProgress()
    {
        Guid versionId, targetId;
        using (var scope = factory.Services.CreateScope())
        {
            var entry = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().VehicleTreeEntries.OrderBy(e => e.RpCost).FirstAsync();
            versionId = entry.ResearchTreeVersionId;
            targetId = entry.VehicleId;
        }
        var request = new ResearchCalculationRequest(versionId, targetId, [], [], 1000, 5000, ResearchOptimizationMode.FewestMatches);
        var before = await _client.PostAsJsonAsync("/api/research/calculate", request);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        var player = await RegisterAsync();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, "/api/player/progress", player.Token,
            new UpdatePlayerProgressRequest([targetId], new(ResearchOptimizationMode.LeastSl, 1, 1)))).StatusCode);
        var after = await _client.PostAsJsonAsync("/api/research/calculate", request);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Equal(await before.Content.ReadAsStringAsync(), await after.Content.ReadAsStringAsync());
    }
}
