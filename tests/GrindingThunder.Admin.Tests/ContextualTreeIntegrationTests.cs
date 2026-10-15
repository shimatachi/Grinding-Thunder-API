using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GrindingThunder.Admin.Tests;

public class ContextualTreeIntegrationTests
{
    private static string NationPath => $"/Trees/{TestMockApiHandler.NationId}";
    private static string TreePath => $"{NationPath}/{TestMockApiHandler.ResearchTreeId}";
    private static string VehiclesPath => $"{TreePath}/Vehicles";
    private static HttpClient Client(AdminWebApplicationFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false, HandleCookies = true
    });

    private static async Task LoginAsync(HttpClient client, string username = "admin")
    {
        var token = await TokenAsync(client, "/Login");
        var response = await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Username"] = username, ["Input.Password"] = $"{username}123", ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task<string> TokenAsync(HttpClient client, string path) =>
        await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(await client.GetAsync(path));

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    [InlineData("publisher")]
    public async Task Navigation_FollowsNationCategoryVehiclesAndVersions(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = Client(factory);
        await LoginAsync(client, username);
        var root = await client.GetStringAsync("/Trees");
        Assert.Contains($"href=\"{NationPath}\"", root);
        Assert.DoesNotContain("2.50", root);

        var nation = await client.GetStringAsync(NationPath);
        Assert.Contains("USA - Aviation", nation);
        Assert.DoesNotContain("Germany - Ground", nation);
        Assert.Contains($"href=\"{VehiclesPath}\"", nation);
        Assert.Contains($"href=\"{TreePath}\"", nation);
        Assert.True((username != "publisher") == nation.Contains("handler=EnableCategory"), nation);

        var vehicles = await client.GetStringAsync(VehiclesPath);
        Assert.Contains("F-16A", vehicles);
        Assert.Contains("F-4E", vehicles);
        Assert.DoesNotContain("Other tree vehicle", vehicles);
        Assert.Contains($"href=\"{NationPath}\"", vehicles);
        Assert.Contains($"href=\"{TreePath}\"", vehicles);
        Assert.Contains("href=\"/Trees\"", vehicles);
        Assert.Equal(username != "publisher", vehicles.Contains("Create vehicle"));
        Assert.Equal(username != "publisher", vehicles.Contains($"/Vehicles/{TestMockApiHandler.VehicleId}/Edit"));
        Assert.Equal(username == "admin", vehicles.Contains($"/Vehicles/{TestMockApiHandler.VehicleId}/Delete"));

        var versions = await client.GetStringAsync(TreePath);
        Assert.Contains("2.50", versions);
        Assert.Contains("2.49", versions);
        Assert.Equal(username != "publisher", versions.Contains("handler=CreateDraft"));
        if (username == "publisher")
        {
            Assert.DoesNotContain(factory.MockApiHandler.RecordedRequests, r =>
                r.RequestUri!.AbsolutePath.StartsWith("/api/admin/") &&
                !r.RequestUri.AbsolutePath.StartsWith("/api/admin/vehicles/tree/"));
            var request = factory.MockApiHandler.RecordedRequests.Single(r => r.RequestUri!.AbsolutePath.StartsWith("/api/admin/vehicles/tree/"));
            Assert.Equal("test-publisher-token", request.Headers.Authorization?.Parameter);
        }
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    public async Task EnableCategory_UsesRouteNationAndOnlyMissingMasterCategories(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = Client(factory);
        await LoginAsync(client, username);
        var response = await client.GetAsync(NationPath);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains($"value=\"{TestMockApiHandler.GroundTypeId}\"", html);
        Assert.DoesNotContain($"value=\"{TestMockApiHandler.VehicleTypeId}\"", html);
        Assert.DoesNotContain("name=\"nationId\"", html);
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(response);
        var formAction = Regex.Match(html, "<form[^>]+action=\"([^\"]*handler=EnableCategory[^\"]*)\"");
        Assert.True(formAction.Success, "The category form must submit to its handler.");
        var post = await client.PostAsync(WebUtility.HtmlDecode(formAction.Groups[1].Value), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["vehicleTypeId"] = TestMockApiHandler.GroundTypeId.ToString(),
            ["nationId"] = TestMockApiHandler.OtherNationId.ToString(),
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Equal(NationPath, post.Headers.Location?.OriginalString);
        var recorded = factory.MockApiHandler.RecordedJsonRequests.Single(r => r.Path == "/api/admin/research-trees");
        using var json = JsonDocument.Parse(recorded.Body);
        Assert.Equal(TestMockApiHandler.NationId, json.RootElement.GetPropertyIgnoreCase("nationId").GetGuid());
        Assert.Equal(TestMockApiHandler.GroundTypeId, json.RootElement.GetPropertyIgnoreCase("vehicleTypeId").GetGuid());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EnableCategory_RejectsExistingOrUnknownCategory(bool existing)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = Client(factory);
        await LoginAsync(client);
        var token = await TokenAsync(client, NationPath);
        factory.MockApiHandler.RecordedRequests.Clear();
        var response = await client.PostAsync($"{NationPath}?handler=EnableCategory", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["vehicleTypeId"] = (existing ? TestMockApiHandler.VehicleTypeId : Guid.NewGuid()).ToString(),
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Choose an available category", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(factory.MockApiHandler.RecordedRequests, r => r.Method == HttpMethod.Post);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    public async Task CreateVehicle_UsesRouteOwnershipAndReturnsToSelectedTree(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = Client(factory);
        await LoginAsync(client, username);
        var form = await client.GetAsync($"{VehiclesPath}/Create");
        var html = await form.Content.ReadAsStringAsync();
        Assert.Contains("USA - Aviation", html);
        Assert.DoesNotContain("Input.ResearchTreeId", html);
        var response = await client.PostAsync($"{VehiclesPath}/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Name"] = "New aircraft", ["Input.ImageUrl"] = "https://example.test/new.png",
            ["Input.ResearchTreeId"] = TestMockApiHandler.OtherTreeId.ToString(),
            ["researchTreeId"] = TestMockApiHandler.OtherTreeId.ToString(),
            ["__RequestVerificationToken"] = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(form)
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(VehiclesPath, response.Headers.Location?.OriginalString);
        using var json = JsonDocument.Parse(factory.MockApiHandler.RecordedJsonRequests.Single(r => r.Path == "/api/admin/vehicles").Body);
        Assert.Equal(TestMockApiHandler.ResearchTreeId, json.RootElement.GetPropertyIgnoreCase("researchTreeId").GetGuid());
        Assert.Equal("New aircraft", json.RootElement.GetPropertyIgnoreCase("name").GetString());
        Assert.Equal("https://example.test/new.png", json.RootElement.GetPropertyIgnoreCase("imageUrl").GetString());
    }

    [Fact]
    public async Task EditVehicle_PreservesFieldsAndNeverSendsReparenting()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = Client(factory);
        await LoginAsync(client, "editor");
        var path = $"{VehiclesPath}/{TestMockApiHandler.VehicleId}/Edit";
        var form = await client.GetAsync(path);
        var html = await form.Content.ReadAsStringAsync();
        Assert.Contains("F-16A", html);
        Assert.Contains("https://example.test/f16.png", html);
        Assert.DoesNotContain("Input.ResearchTreeId", html);
        var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Name"] = "F-16A revised", ["Input.ImageUrl"] = "https://example.test/revised.png",
            ["Input.ResearchTreeId"] = TestMockApiHandler.OtherTreeId.ToString(),
            ["__RequestVerificationToken"] = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(form)
        }));
        Assert.Equal(VehiclesPath, response.Headers.Location?.OriginalString);
        using var json = JsonDocument.Parse(factory.MockApiHandler.RecordedJsonRequests.Single(r => r.Path == $"/api/admin/vehicles/{TestMockApiHandler.VehicleId}").Body);
        Assert.Equal("F-16A revised", json.RootElement.GetPropertyIgnoreCase("name").GetString());
        Assert.Equal("https://example.test/revised.png", json.RootElement.GetPropertyIgnoreCase("imageUrl").GetString());
        Assert.DoesNotContain(json.RootElement.EnumerateObject(), p => p.Name.Equals("researchTreeId", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Edit")]
    [InlineData("Delete")]
    [InlineData("EnableCategory")]
    public async Task Publisher_DirectMutationAttemptsMakeNoApiCalls(string action)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = Client(factory);
        await LoginAsync(client, "publisher");
        var token = await TokenAsync(client, NationPath);
        var path = action switch
        {
            "EnableCategory" => $"{NationPath}?handler=EnableCategory",
            "Create" => $"{VehiclesPath}/Create",
            _ => $"{VehiclesPath}/{TestMockApiHandler.VehicleId}/{action}"
        };
        factory.MockApiHandler.RecordedRequests.Clear();
        var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Name"] = "Forbidden", ["vehicleTypeId"] = TestMockApiHandler.GroundTypeId.ToString(), ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/AccessDenied", response.Headers.Location is { IsAbsoluteUri: true } uri ? uri.PathAndQuery : response.Headers.Location?.OriginalString);
        Assert.Empty(factory.MockApiHandler.RecordedRequests);
    }

    [Theory]
    [InlineData("Edit", false)]
    [InlineData("Edit", true)]
    [InlineData("Delete", false)]
    [InlineData("Delete", true)]
    public async Task ForeignVehicle_IsRejectedBeforeMutation(string action, bool post)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = Client(factory);
        await LoginAsync(client);
        var token = await TokenAsync(client, VehiclesPath);
        factory.MockApiHandler.RecordedRequests.Clear();
        var path = $"{VehiclesPath}/{TestMockApiHandler.OtherVehicleId}/{action}";
        var response = post ? await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Name"] = "Forged", ["__RequestVerificationToken"] = token
        })) : await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(factory.MockApiHandler.RecordedRequests, r => r.Method != HttpMethod.Get);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/Vehicles")]
    [InlineData("/Vehicles/Create")]
    public async Task TreeUnderWrongNation_Returns404(string suffix)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = Client(factory);
        await LoginAsync(client);
        var response = await client.GetAsync($"/Trees/{TestMockApiHandler.OtherNationId}/{TestMockApiHandler.ResearchTreeId}{suffix}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("CreateDraft")]
    [InlineData("Clone")]
    [InlineData("Delete")]
    public async Task ContextualVersionForms_SubmitToTheirHandlers(string handler)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = Client(factory);
        await LoginAsync(client);
        var form = await client.GetAsync(TreePath);
        var html = await form.Content.ReadAsStringAsync();
        var action = Regex.Match(html, $"<form[^>]+action=\"([^\"]*handler={handler}[^\"]*)\"");
        Assert.True(action.Success, $"The {handler} form must submit to its handler.");
        var response = await client.PostAsync(WebUtility.HtmlDecode(action.Groups[1].Value), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["researchTreeId"] = TestMockApiHandler.ResearchTreeId.ToString(),
            ["versionId"] = TestMockApiHandler.DraftVersionId.ToString(),
            ["gameUpdateId"] = TestMockApiHandler.GameUpdateId.ToString(),
            ["targetGameUpdateId"] = TestMockApiHandler.GameUpdate2Id.ToString(),
            ["__RequestVerificationToken"] = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(form)
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(handler == "Delete" ? TreePath : $"/Trees/Version/{TestMockApiHandler.DraftVersionId}", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VersionMutation_RejectsMismatchedTreeOrVersion(bool foreignVersion)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = Client(factory);
        await LoginAsync(client);
        var token = await TokenAsync(client, TreePath);
        factory.MockApiHandler.RecordedRequests.Clear();
        var response = await client.PostAsync($"{TreePath}?handler=Clone", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["researchTreeId"] = (foreignVersion ? TestMockApiHandler.ResearchTreeId : TestMockApiHandler.OtherTreeId).ToString(),
            ["versionId"] = (foreignVersion ? Guid.NewGuid() : TestMockApiHandler.DraftVersionId).ToString(),
            ["targetGameUpdateId"] = TestMockApiHandler.GameUpdate2Id.ToString(),
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(factory.MockApiHandler.RecordedRequests, r => r.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task EnableCategory_WhenApiConflicts_KeepsNationAndForm()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = Client(factory);
        await LoginAsync(client);
        var token = await TokenAsync(client, NationPath);
        factory.MockApiHandler.ConflictNextCatalogMutation = true;
        var response = await client.PostAsync($"{NationPath}?handler=EnableCategory", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["vehicleTypeId"] = TestMockApiHandler.GroundTypeId.ToString(), ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("A matching catalog item already exists.", html);
        Assert.Contains("USA - Categories", html);
        Assert.Contains("handler=EnableCategory", html);
    }

    [Fact]
    public async Task Editor_CannotDeleteVehicle_EvenByDirectPost()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = Client(factory);
        await LoginAsync(client, "editor");
        var token = await TokenAsync(client, VehiclesPath);
        factory.MockApiHandler.RecordedRequests.Clear();
        var response = await client.PostAsync($"{VehiclesPath}/{TestMockApiHandler.VehicleId}/Delete", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/AccessDenied", response.Headers.Location?.OriginalString);
        Assert.Empty(factory.MockApiHandler.RecordedRequests);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    [InlineData("publisher")]
    public async Task AdministrationMenu_ReflectsRoleAndGroupsReferenceData(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = Client(factory);
        await LoginAsync(client, username);
        var html = await client.GetStringAsync("/");
        Assert.Equal(username != "publisher", html.Contains("id=\"administration-menu\""));
        Assert.Equal(username == "admin", html.Contains(">Admin Accounts</a>"));
        if (username != "publisher")
        {
            var administration = await client.GetStringAsync("/Administration");
            Assert.Contains("href=\"/Catalog/Nations\"", administration);
            Assert.Contains("href=\"/Catalog/Vehicle-Types\"", administration);
            Assert.Equal(username == "admin", administration.Contains("href=\"/Accounts\""));
        }
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    [InlineData("publisher")]
    public async Task VersionAndComparison_BackLinksPreserveOwningTree(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = Client(factory);
        await LoginAsync(client, username);
        var version = await client.GetStringAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        Assert.Contains($"href=\"{TreePath}\">Back to versions", version);
        Assert.Contains($"href=\"{VehiclesPath}\"", version);
        var comparison = await client.GetStringAsync($"/Trees/Diff?researchTreeId={TestMockApiHandler.ResearchTreeId}");
        Assert.Contains($"href=\"{TreePath}\">Back to versions", comparison);
    }
}
