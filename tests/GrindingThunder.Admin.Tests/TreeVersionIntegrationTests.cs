using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GrindingThunder.Admin.Tests;

public class TreeVersionIntegrationTests
{
    [Fact]
    public async Task TreesIndex_ListsDraftAndPublishedVersionsForSelectedTree()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client);

        var response = await client.GetAsync($"/Trees?researchTreeId={TestMockApiHandler.ResearchTreeId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("2.50", html);
        Assert.Contains("2.49", html);
        Assert.Contains("Draft", html);
        Assert.Contains("Published", html);
        Assert.Contains("handler=CreateDraft", html);
        Assert.Contains("handler=Clone", html);
        Assert.Contains("handler=Delete", html);
        Assert.Contains("onsubmit=\"return confirm('Delete this draft version and all its ranks, entries, and prerequisites?');\"", html);
    }

    [Fact]
    public async Task TreesIndex_WithoutSelection_DefaultsToFirstTree()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client);

        var response = await client.GetAsync("/Trees");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("USA", html);
        Assert.Contains("Aviation", html);
        Assert.Contains("2.50", html);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    public async Task CreateDraft_InvokesVersionLifecycleApiAndRedirectsToEditor(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);
        var form = await client.GetAsync("/Trees");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(form);

        var response = await client.PostAsync("/Trees?handler=CreateDraft", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["researchTreeId"] = TestMockApiHandler.ResearchTreeId.ToString(),
            ["gameUpdateId"] = TestMockApiHandler.GameUpdateId.ToString(),
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(TestMockApiHandler.DraftVersionId.ToString(), response.Headers.Location?.OriginalString);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/api/admin/research-tree-versions");
    }

    [Fact]
    public async Task CreateDraft_WhenApiReturnsConflict_RendersErrorMessage()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client);
        var form = await client.GetAsync("/Trees");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(form);
        factory.MockApiHandler.ConflictNextCatalogMutation = true;

        var response = await client.PostAsync("/Trees?handler=CreateDraft", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["researchTreeId"] = TestMockApiHandler.ResearchTreeId.ToString(),
            ["gameUpdateId"] = TestMockApiHandler.GameUpdateId.ToString(),
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var followUp = await client.GetAsync($"/Trees?researchTreeId={TestMockApiHandler.ResearchTreeId}");
        var html = await followUp.Content.ReadAsStringAsync();

        Assert.Contains("A matching catalog item already exists.", html);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    public async Task CloneVersion_InvokesCloneEndpoint(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);
        var form = await client.GetAsync("/Trees");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(form);

        var response = await client.PostAsync("/Trees?handler=Clone", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["versionId"] = TestMockApiHandler.PublishedVersionId.ToString(),
            ["targetGameUpdateId"] = TestMockApiHandler.GameUpdate2Id.ToString(),
            ["researchTreeId"] = TestMockApiHandler.ResearchTreeId.ToString(),
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.Method == HttpMethod.Post &&
            request.RequestUri?.AbsolutePath == $"/api/admin/research-tree-versions/{TestMockApiHandler.PublishedVersionId}/clone");
    }

    [Fact]
    public async Task DeleteDraft_InvokesDeleteEndpoint()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client);
        var form = await client.GetAsync("/Trees");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(form);

        var response = await client.PostAsync("/Trees?handler=Delete", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["versionId"] = TestMockApiHandler.DraftVersionId.ToString(),
            ["researchTreeId"] = TestMockApiHandler.ResearchTreeId.ToString(),
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.Method == HttpMethod.Delete &&
            request.RequestUri?.AbsolutePath == $"/api/admin/research-tree-versions/{TestMockApiHandler.DraftVersionId}");
    }

    [Fact]
    public async Task DeletePublishedVersion_ShowsConflictError()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client);
        var form = await client.GetAsync("/Trees");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(form);

        var response = await client.PostAsync("/Trees?handler=Delete", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["versionId"] = TestMockApiHandler.PublishedVersionId.ToString(),
            ["researchTreeId"] = TestMockApiHandler.ResearchTreeId.ToString(),
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var followUp = await client.GetAsync($"/Trees?researchTreeId={TestMockApiHandler.ResearchTreeId}");
        var html = await followUp.Content.ReadAsStringAsync();

        Assert.Contains("Published versions cannot be deleted.", html);
    }

    [Theory]
    [InlineData("admin", "Admin")]
    [InlineData("editor", "Editor")]
    [InlineData("publisher", "Publisher")]
    public async Task MockLoginAndProfile_ReturnOnlyTheAccountRole(string username, string expectedRole)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);
        var html = await (await client.GetAsync("/")).Content.ReadAsStringAsync();
        Assert.Contains($">{expectedRole}</span>", html);
        foreach (var role in new[] { "Admin", "Editor", "Publisher" }.Where(role => role != expectedRole))
            Assert.DoesNotContain($">{role}</span>", html);
    }

    [Theory]
    [InlineData("editor")]
    [InlineData("publisher")]
    public async Task NonAdmin_CannotDeleteDraft_EvenWithDirectPost(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);
        var page = await client.GetAsync("/Trees");
        var html = await page.Content.ReadAsStringAsync();
        Assert.DoesNotContain("handler=Delete", html);
        Assert.Equal(username == "editor", html.Contains("handler=CreateDraft"));
        Assert.Equal(username == "editor", html.Contains("handler=Clone"));
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);
        factory.MockApiHandler.RecordedRequests.Clear();

        var response = await client.PostAsync("/Trees?handler=Delete", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["versionId"] = TestMockApiHandler.DraftVersionId.ToString(),
            ["researchTreeId"] = TestMockApiHandler.ResearchTreeId.ToString(),
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/AccessDenied", response.Headers.Location?.OriginalString);
        Assert.Empty(factory.MockApiHandler.RecordedRequests);
    }

    [Theory]
    [InlineData("CreateDraft")]
    [InlineData("Clone")]
    public async Task Publisher_CannotInvokeEditorLifecycleHandlers(string handler)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, "publisher");
        var page = await client.GetAsync("/Trees");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);
        factory.MockApiHandler.RecordedRequests.Clear();

        var response = await client.PostAsync($"/Trees?handler={handler}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["versionId"] = TestMockApiHandler.PublishedVersionId.ToString(),
            ["researchTreeId"] = TestMockApiHandler.ResearchTreeId.ToString(),
            ["gameUpdateId"] = TestMockApiHandler.GameUpdateId.ToString(),
            ["targetGameUpdateId"] = TestMockApiHandler.GameUpdate2Id.ToString(),
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/AccessDenied", response.Headers.Location?.OriginalString);
        Assert.Empty(factory.MockApiHandler.RecordedRequests);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    [InlineData("publisher")]
    public async Task PublishingControl_OnlyAppearsOnDraftForPublisherOrAdmin(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);
        var draft = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var draftHtml = await draft.Content.ReadAsStringAsync();
        Assert.Equal(username != "editor", draftHtml.Contains("handler=Publish"));
        var published = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.PublishedVersionId}");
        Assert.DoesNotContain("handler=Publish", await published.Content.ReadAsStringAsync());
        Assert.DoesNotContain(factory.MockApiHandler.RecordedRequests, request =>
            request.RequestUri?.AbsolutePath.EndsWith("/publish", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Publisher_TreesIndex_SucceedsWithoutCallingEditorEndpoints()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, "publisher");
        factory.MockApiHandler.RecordedRequests.Clear();

        var response = await client.GetAsync($"/Trees?researchTreeId={TestMockApiHandler.ResearchTreeId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("USA", html);
        Assert.Contains("Aviation", html);
        Assert.Contains("2.50", html);
        Assert.Contains("2.49", html);
        Assert.Contains("Draft", html);
        Assert.Contains("Published", html);
        Assert.DoesNotContain("handler=CreateDraft", html);
        Assert.DoesNotContain("handler=Clone", html);
        Assert.DoesNotContain("handler=Delete", html);

        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.RequestUri?.AbsolutePath == "/api/nations");
        Assert.DoesNotContain(factory.MockApiHandler.RecordedRequests, request =>
            request.RequestUri?.AbsolutePath == "/api/admin/research-trees");
        Assert.DoesNotContain(factory.MockApiHandler.RecordedRequests, request =>
            request.RequestUri?.AbsolutePath == "/api/admin/game-updates");
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    public async Task EditorAndAdmin_TreesIndex_UsesSecuredAdminEndpoints(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);
        factory.MockApiHandler.RecordedRequests.Clear();

        var response = await client.GetAsync($"/Trees?researchTreeId={TestMockApiHandler.ResearchTreeId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.RequestUri?.AbsolutePath == "/api/admin/research-trees");
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.RequestUri?.AbsolutePath == "/api/admin/game-updates");
    }

    private static WebApplicationFactoryClientOptions ClientOptions() => new()
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    };

    private static async Task LoginAsync(HttpClient client, string username = "admin")
    {
        var loginPage = await client.GetAsync("/Login");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(loginPage);
        var response = await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Username"] = username,
            ["Input.Password"] = $"{username}123",
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
