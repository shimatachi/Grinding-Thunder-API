using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GrindingThunder.Admin.Tests;

public class CatalogIntegrationTests
{
    [Theory]
    [InlineData("/Catalog/Nations", "USA")]
    [InlineData("/Catalog/Vehicle-Types", "Aviation")]
    [InlineData("/Trees/11111111-1111-1111-1111-111111111111/55555555-5555-5555-5555-555555555555/Vehicles", "F-16A")]
    [InlineData("/Catalog/Game-Updates", "2.50")]
    [InlineData("/Catalog/Research-Trees", "USA")]
    public async Task CatalogList_RendersApiData(string path, string expectedText)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedText, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [MemberData(nameof(CreateCases))]
    public async Task CatalogCreate_PostsToExpectedEndpoint(
        string pagePath,
        string apiPath,
        Dictionary<string, string> fields)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client);
        var form = await client.GetAsync(pagePath);
        fields["__RequestVerificationToken"] = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(form);

        var response = await client.PostAsync(pagePath, new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == apiPath);
    }

    [Theory]
    [MemberData(nameof(EditCases))]
    public async Task CatalogEdit_LoadsAndUpdatesEntity(
        string pagePath,
        string apiPath,
        Dictionary<string, string> fields)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client);
        var form = await client.GetAsync(pagePath);
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        fields["__RequestVerificationToken"] = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(form);

        var response = await client.PostAsync(pagePath, new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.Method == HttpMethod.Put && request.RequestUri?.AbsolutePath == apiPath);
    }

    [Theory]
    [MemberData(nameof(CreateCases))]
    public async Task CatalogCreate_WhenApiReturnsConflict_RendersMessageAndKeepsForm(
        string pagePath,
        string apiPath,
        Dictionary<string, string> fields)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client);
        var form = await client.GetAsync(pagePath);
        fields["__RequestVerificationToken"] = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(form);
        factory.MockApiHandler.ConflictNextCatalogMutation = true;

        var response = await client.PostAsync(pagePath, new FormUrlEncodedContent(fields));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("A matching catalog item already exists.", html);
        Assert.Contains(fields.Values.First(value => value != fields["__RequestVerificationToken"]), html);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request => request.RequestUri?.AbsolutePath == apiPath);
    }

    [Fact]
    public async Task Delete_WhenApiReturnsConflict_RendersInUseMessage()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client);
        var path = $"/Trees/{TestMockApiHandler.NationId}/{TestMockApiHandler.ResearchTreeId}/Vehicles/{TestMockApiHandler.VehicleId}/Delete";
        var form = await client.GetAsync(path);
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(form);
        factory.MockApiHandler.ConflictNextDelete = true;

        var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Cannot delete this catalog item because it is in use.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CatalogCall_PropagatesBearerToken()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client);

        var response = await client.GetAsync("/Catalog/Nations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = factory.MockApiHandler.RecordedRequests.Last(r => r.RequestUri?.AbsolutePath == "/api/admin/nations");
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("test-jwt-token-xyz", request.Headers.Authorization?.Parameter);
    }

    public static TheoryData<string, string, Dictionary<string, string>> CreateCases => new()
    {
        { "/Catalog/Nations/Create", "/api/admin/nations", new() { ["Input.Name"] = "Canada" } },
        { "/Catalog/Vehicle-Types/Create", "/api/admin/vehicle-types", new() { ["Input.Name"] = "Ground" } },
        { $"/Trees/{TestMockApiHandler.NationId}/{TestMockApiHandler.ResearchTreeId}/Vehicles/Create", "/api/admin/vehicles", new() { ["Input.Name"] = "Leopard 2A7", ["Input.ImageUrl"] = "https://example.test/leopard.png" } },
        { "/Catalog/Game-Updates/Create", "/api/admin/game-updates", new() { ["Input.Version"] = "2.51", ["Input.Name"] = "Next Update", ["Input.ReleaseDate"] = "2026-11-01" } },
        { "/Catalog/Research-Trees/Create", "/api/admin/research-trees", new() { ["Input.NationId"] = TestMockApiHandler.NationId.ToString(), ["Input.VehicleTypeId"] = TestMockApiHandler.VehicleTypeId.ToString() } }
    };

    public static TheoryData<string, string, Dictionary<string, string>> EditCases => new()
    {
        { $"/Catalog/Nations/{TestMockApiHandler.NationId}/Edit", $"/api/admin/nations/{TestMockApiHandler.NationId}", new() { ["Input.Name"] = "United States" } },
        { $"/Catalog/Vehicle-Types/{TestMockApiHandler.VehicleTypeId}/Edit", $"/api/admin/vehicle-types/{TestMockApiHandler.VehicleTypeId}", new() { ["Input.Name"] = "Military Aviation" } },
        { $"/Trees/{TestMockApiHandler.NationId}/{TestMockApiHandler.ResearchTreeId}/Vehicles/{TestMockApiHandler.VehicleId}/Edit", $"/api/admin/vehicles/{TestMockApiHandler.VehicleId}", new() { ["Input.Name"] = "F-16A ADF", ["Input.ImageUrl"] = "https://example.test/f16-adf.png" } },
        { $"/Catalog/Game-Updates/{TestMockApiHandler.GameUpdateId}/Edit", $"/api/admin/game-updates/{TestMockApiHandler.GameUpdateId}", new() { ["Input.Version"] = "2.50.1", ["Input.Name"] = "Test Update Revised", ["Input.ReleaseDate"] = "2026-10-13" } }
    };

    private static WebApplicationFactoryClientOptions ClientOptions() => new()
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    };

    private static async Task LoginAsync(HttpClient client)
    {
        var loginPage = await client.GetAsync("/Login");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(loginPage);
        var response = await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Username"] = "admin",
            ["Input.Password"] = "admin123",
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
