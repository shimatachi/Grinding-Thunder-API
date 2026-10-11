using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Application.Security;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class AuthIntegrationTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.InitializeDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Fact]
    public async Task Login_WithValidCredentials_Returns200WithBearerTokenAndRoles()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", "AdminPass123!"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();

        Assert.NotNull(result);
        Assert.NotEmpty(result.Token);
        Assert.Equal("Bearer", result.TokenType);
        Assert.Equal("admin", result.Username);
        Assert.Contains(AuthRoles.Admin, result.Roles);
        Assert.True(result.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task Login_WithInvalidPassword_Returns401Unauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", "WrongPassword!"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownUsername_Returns401Unauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("unknownUser", "AdminPass123!"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("user", "")]
    [InlineData("  ", "  ")]
    public async Task Login_WithEmptyCredentials_Returns400BadRequest(string username, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_WithoutToken_Returns401Unauthorized()
    {
        var response = await _client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_WithValidToken_Returns200WithUsernameAndRoles()
    {
        var token = await _factory.GetTokenAsync("editor", "EditorPass123!");
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/auth/me", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();

        Assert.NotNull(user);
        Assert.Equal("editor", user.Username);
        Assert.Contains(AuthRoles.Editor, user.Roles);
    }

    [Fact]
    public async Task EditorCheck_WithoutToken_Returns401Unauthorized()
    {
        var response = await _client.GetAsync("/api/auth/editor-check");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EditorCheck_WithEditorRole_Returns200Ok()
    {
        var token = await _factory.GetTokenAsync("editor", "EditorPass123!");
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/auth/editor-check", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EditorCheck_WithAdminRole_Returns200Ok()
    {
        var token = await _factory.GetTokenAsync("admin", "AdminPass123!");
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/auth/editor-check", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EditorCheck_WithPublisherRole_Returns403Forbidden()
    {
        var token = await _factory.GetTokenAsync("publisher", "PublisherPass123!");
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/auth/editor-check", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PublisherCheck_WithoutToken_Returns401Unauthorized()
    {
        var response = await _client.GetAsync("/api/auth/publisher-check");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PublisherCheck_WithPublisherRole_Returns200Ok()
    {
        var token = await _factory.GetTokenAsync("publisher", "PublisherPass123!");
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/auth/publisher-check", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PublisherCheck_WithAdminRole_Returns200Ok()
    {
        var token = await _factory.GetTokenAsync("admin", "AdminPass123!");
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/auth/publisher-check", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PublisherCheck_WithEditorRole_Returns403Forbidden()
    {
        var token = await _factory.GetTokenAsync("editor", "EditorPass123!");
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/auth/publisher-check", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminCheck_WithoutToken_Returns401Unauthorized()
    {
        var response = await _client.GetAsync("/api/auth/admin-check");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminCheck_WithAdminRole_Returns200Ok()
    {
        var token = await _factory.GetTokenAsync("admin", "AdminPass123!");
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/auth/admin-check", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminCheck_WithEditorRole_Returns403Forbidden()
    {
        var token = await _factory.GetTokenAsync("editor", "EditorPass123!");
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/auth/admin-check", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminCheck_WithPublisherRole_Returns403Forbidden()
    {
        var token = await _factory.GetTokenAsync("publisher", "PublisherPass123!");
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/auth/admin-check", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PublicEndpoints_Nations_Returns200WithoutAuth()
    {
        var response = await _client.GetAsync("/api/nations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PublicEndpoints_Vehicles_Returns200WithoutAuth()
    {
        var response = await _client.GetAsync("/api/vehicles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PublicEndpoints_Calculate_DoesNotRequireAuth()
    {
        // Bad request due to missing fields, verifying endpoint is reachable and not challenged with 401
        var request = new ResearchCalculationRequest(
            ResearchTreeVersionId: Guid.Empty,
            TargetVehicleId: Guid.Empty,
            UnlockedVehicleIds: [],
            FillerTargetIds: [],
            AverageRpPerMatch: 1000,
            AverageNetSlPerMatch: 5000);

        var response = await _client.PostAsJsonAsync("/api/research/calculate", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PublicEndpoints_Diff_DoesNotRequireAuth()
    {
        // Bad request due to missing query params, verifying endpoint is reachable and not challenged with 401
        var response = await _client.GetAsync("/api/research-tree-versions/diff");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PublicEndpoints_Validate_DoesNotRequireAuth()
    {
        // 404 for non-existent version, verifying endpoint is reachable and not challenged with 401
        var response = await _client.GetAsync($"/api/research-tree-versions/{Guid.NewGuid()}/validate");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
