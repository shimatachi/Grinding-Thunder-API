using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrindingThunder.Admin.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GrindingThunder.Admin.Tests;

public class PlayerProgressionOverlayTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task AnonymousUser_CanCalculateResearchRouteWithoutLogin()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient();

        // Anonymous user visits /Tree
        var pageResponse = await client.GetAsync($"/Tree?nationId={TestMockApiHandler.NationId}&researchTreeId={TestMockApiHandler.ResearchTreeId}&versionId={TestMockApiHandler.PublishedVersionId}");
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        var html = await pageResponse.Content.ReadAsStringAsync();
        Assert.Contains("Player sign in", html);
        Assert.Contains("initial-player-state", html);
        Assert.Contains("\"isAuthenticated\":false", html);

        // Calculate route to F-16A with no owned vehicles -> requires F-4E prerequisite + F-16A
        var calcRequest1 = new ResearchCalculationRequest(
            ResearchTreeVersionId: TestMockApiHandler.PublishedVersionId,
            TargetVehicleId: TestMockApiHandler.VehicleId,
            UnlockedVehicleIds: [],
            FillerTargetIds: [],
            AverageRpPerMatch: 5000,
            AverageNetSlPerMatch: 20000,
            OptimizationMode: ResearchOptimizationMode.LeastRp);

        var calcResponse1 = await client.PostAsJsonAsync("/Tree?handler=Calculate", calcRequest1);
        var content1 = await calcResponse1.Content.ReadAsStringAsync();
        Assert.True(calcResponse1.IsSuccessStatusCode, $"Status: {calcResponse1.StatusCode}, Content: {content1}");
        var result1 = await calcResponse1.Content.ReadFromJsonAsync<ResearchCalculationResult>(JsonOptions);
        Assert.NotNull(result1);
        Assert.Equal(TestMockApiHandler.VehicleId, result1.TargetVehicleId);
        Assert.Equal(400000, result1.TotalRpRequired);
        Assert.Equal(640000, result1.TotalSlRequired);
        Assert.Equal(2, result1.RequiredVehicles.Count);
        Assert.Contains(result1.RequiredVehicles, v => v.VehicleId == TestMockApiHandler.VehicleId2 && v.Name == "F-4E");
        Assert.Contains(result1.RequiredVehicles, v => v.VehicleId == TestMockApiHandler.VehicleId && v.Name == "F-16A");

        // Temporary in-memory unlock of prerequisite F-4E
        var calcRequest2 = calcRequest1 with { UnlockedVehicleIds = [TestMockApiHandler.VehicleId2] };
        var calcResponse2 = await client.PostAsJsonAsync("/Tree?handler=Calculate", calcRequest2);
        Assert.Equal(HttpStatusCode.OK, calcResponse2.StatusCode);
        var result2 = await calcResponse2.Content.ReadFromJsonAsync<ResearchCalculationResult>(JsonOptions);
        Assert.NotNull(result2);
        Assert.Equal(220000, result2.TotalRpRequired);
        Assert.Equal(350000, result2.TotalSlRequired);
        Assert.Single(result2.RequiredVehicles);
        Assert.Equal(TestMockApiHandler.VehicleId, result2.RequiredVehicles[0].VehicleId);
    }

    [Fact]
    public async Task PlayerLoginAndRegister_IssuesPlayerAuthCookie_AndIsolatesAdminAuth()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // 1. Attempt login with invalid password
        var invalidLogin = await client.PostAsJsonAsync("/Tree?handler=PlayerLogin", new PlayerLoginRequest("testplayer", "InvalidPass"));
        Assert.Equal(HttpStatusCode.Unauthorized, invalidLogin.StatusCode);

        // 2. Successful login
        var loginResponse = await client.PostAsJsonAsync("/Tree?handler=PlayerLogin", new PlayerLoginRequest("testplayer", "PlayerPass123!"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginDoc = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(loginDoc.GetProperty("success").GetBoolean());
        Assert.Equal("testplayer", loginDoc.GetProperty("username").GetString());

        // Verify Set-Cookie header contains GrindingThunder.Player.Session
        Assert.True(loginResponse.Headers.Contains("Set-Cookie"));
        var cookies = string.Join("; ", loginResponse.Headers.GetValues("Set-Cookie"));
        Assert.Contains("GrindingThunder.Player.Session", cookies);

        // 3. Verify strict isolation: player session CANNOT access admin dashboard
        var adminDashboard = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, adminDashboard.StatusCode);
        Assert.Contains("/Login", adminDashboard.Headers.Location?.OriginalString);

        // 4. Test player registration
        var regResponse = await client.PostAsJsonAsync("/Tree?handler=PlayerRegister", new PlayerRegisterRequest("newplayer", "new@example.test", "SecurePass123!"));
        Assert.Equal(HttpStatusCode.OK, regResponse.StatusCode);
        var regDoc = await regResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(regDoc.GetProperty("success").GetBoolean());
        Assert.Equal("newplayer", regDoc.GetProperty("username").GetString());
    }

    [Fact]
    public async Task AuthenticatedPlayer_LoadsPersistentProgress_AndIntersectsWithTree()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient();

        // Initially player has unlocked OtherVehicleId (out-of-tree) and F-4E (VehicleId2)
        factory.MockApiHandler.PlayerUnlockedVehicleIds.Add(TestMockApiHandler.VehicleId2);

        // Log in
        var loginResponse = await client.PostAsJsonAsync("/Tree?handler=PlayerLogin", new PlayerLoginRequest("testplayer", "PlayerPass123!"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        // Visit /Tree
        var treeResponse = await client.GetAsync($"/Tree?nationId={TestMockApiHandler.NationId}&researchTreeId={TestMockApiHandler.ResearchTreeId}&versionId={TestMockApiHandler.PublishedVersionId}");
        Assert.Equal(HttpStatusCode.OK, treeResponse.StatusCode);
        var html = await treeResponse.Content.ReadAsStringAsync();

        // Player is authenticated and recognized on the page
        Assert.Contains("testplayer", html);
        Assert.Contains("Sign out", html);
        Assert.Contains("\"isAuthenticated\":true", html);
        Assert.Contains(TestMockApiHandler.VehicleId2.ToString(), html);

        // Fetch progress directly via handler
        var progressResponse = await client.GetAsync("/Tree?handler=PlayerProgress");
        Assert.Equal(HttpStatusCode.OK, progressResponse.StatusCode);
        var progress = await progressResponse.Content.ReadFromJsonAsync<PlayerProgressResponse>(JsonOptions);
        Assert.NotNull(progress);
        Assert.Contains(TestMockApiHandler.OtherVehicleId, progress.UnlockedVehicleIds);
        Assert.Contains(TestMockApiHandler.VehicleId2, progress.UnlockedVehicleIds);
    }

    [Fact]
    public async Task TogglingVehicleOwnership_PersistsFullSet_AndPreservesOutOfTreeVehicles()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient();

        // Player starts with OtherVehicleId (out of tree)
        Assert.Contains(TestMockApiHandler.OtherVehicleId, factory.MockApiHandler.PlayerUnlockedVehicleIds);
        Assert.DoesNotContain(TestMockApiHandler.VehicleId2, factory.MockApiHandler.PlayerUnlockedVehicleIds);

        // Sign in
        await client.PostAsJsonAsync("/Tree?handler=PlayerLogin", new PlayerLoginRequest("testplayer", "PlayerPass123!"));

        // Player toggles VehicleId2 (F-4E) on: client sends complete set including OtherVehicleId
        var update1 = new UpdatePlayerProgressRequest(
            UnlockedVehicleIds: [TestMockApiHandler.OtherVehicleId, TestMockApiHandler.VehicleId2],
            Preferences: new PlayerCalculatorPreferences(ResearchOptimizationMode.LeastRp, 5000, 20000));

        var updateResponse1 = await client.PostAsJsonAsync("/Tree?handler=UpdateProgress", update1);
        Assert.Equal(HttpStatusCode.OK, updateResponse1.StatusCode);

        // Backend must contain both vehicles
        Assert.Contains(TestMockApiHandler.OtherVehicleId, factory.MockApiHandler.PlayerUnlockedVehicleIds);
        Assert.Contains(TestMockApiHandler.VehicleId2, factory.MockApiHandler.PlayerUnlockedVehicleIds);

        // Player toggles VehicleId2 off: client sends [OtherVehicleId]
        var update2 = new UpdatePlayerProgressRequest(
            UnlockedVehicleIds: [TestMockApiHandler.OtherVehicleId],
            Preferences: new PlayerCalculatorPreferences(ResearchOptimizationMode.LeastRp, 5000, 20000));

        var updateResponse2 = await client.PostAsJsonAsync("/Tree?handler=UpdateProgress", update2);
        Assert.Equal(HttpStatusCode.OK, updateResponse2.StatusCode);

        // Out-of-tree vehicle is STILL preserved!
        Assert.Contains(TestMockApiHandler.OtherVehicleId, factory.MockApiHandler.PlayerUnlockedVehicleIds);
        Assert.DoesNotContain(TestMockApiHandler.VehicleId2, factory.MockApiHandler.PlayerUnlockedVehicleIds);
    }

    [Fact]
    public async Task ConcurrencyConflict_Returns409_AndAllowsRefetch()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/Tree?handler=PlayerLogin", new PlayerLoginRequest("testplayer", "PlayerPass123!"));

        factory.MockApiHandler.ConflictNextPlayerProgress = true;

        var update = new UpdatePlayerProgressRequest(
            UnlockedVehicleIds: [TestMockApiHandler.OtherVehicleId, TestMockApiHandler.VehicleId],
            Preferences: new PlayerCalculatorPreferences(ResearchOptimizationMode.LeastRp, 5000, 20000));

        var conflictResponse = await client.PostAsJsonAsync("/Tree?handler=UpdateProgress", update);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);

        // Refetch progress succeeds
        var refreshResponse = await client.GetAsync("/Tree?handler=PlayerProgress");
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var data = await refreshResponse.Content.ReadFromJsonAsync<PlayerProgressResponse>(JsonOptions);
        Assert.NotNull(data);
    }

    [Fact]
    public async Task PlayerSignOut_ClearsPlayerSession_AndRevertsToAnonymous()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/Tree?handler=PlayerLogin", new PlayerLoginRequest("testplayer", "PlayerPass123!"));

        // Verify logged in
        var tree1 = await client.GetAsync($"/Tree?nationId={TestMockApiHandler.NationId}&researchTreeId={TestMockApiHandler.ResearchTreeId}&versionId={TestMockApiHandler.PublishedVersionId}");
        var html1 = await tree1.Content.ReadAsStringAsync();
        Assert.Contains("testplayer", html1);
        Assert.Contains("\"isAuthenticated\":true", html1);

        // Logout
        var logoutResponse = await client.PostAsJsonAsync("/Tree?handler=PlayerLogout", new { });
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        // Next visit is anonymous
        var tree2 = await client.GetAsync($"/Tree?nationId={TestMockApiHandler.NationId}&researchTreeId={TestMockApiHandler.ResearchTreeId}&versionId={TestMockApiHandler.PublishedVersionId}");
        var html2 = await tree2.Content.ReadAsStringAsync();
        Assert.Contains("Player sign in", html2);
        Assert.Contains("\"isAuthenticated\":false", html2);
    }
}
