using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GrindingThunder.Admin.Tests;

public class ReleaseWorkspaceIntegrationTests
{
    private static string WorkspacePath => $"/Catalog/Game-Updates/{TestMockApiHandler.GameUpdateId}/Workspace";

    [Fact]
    public async Task GameUpdatesIndex_LinksDirectlyToReleaseWorkspace()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(Options());
        await LoginAsync(client, "editor");
        var html = await (await client.GetAsync("/Catalog/Game-Updates")).Content.ReadAsStringAsync();
        Assert.Contains("Release Workspace", html);
        Assert.Contains(WorkspacePath, html);
    }

    [Fact]
    public async Task Workspace_LoadsMetadataGroupsAndAggregateProgress()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(Options());
        await LoginAsync(client, "admin");
        var response = await client.GetAsync(WorkspacePath);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("2.50", html);
        Assert.Contains("Test Update", html);
        Assert.Contains("2026-10-12", html);
        Assert.Contains("data-nation=\"USA\"", html);
        Assert.Contains("data-nation=\"Germany\"", html);
        Assert.Contains("Aviation", html);
        Assert.Contains("Ground", html);
        Assert.Contains("data-progress=\"total\">2", html);
        Assert.Contains("data-progress=\"published\">0", html);
        Assert.Contains("data-progress=\"valid-draft\">1", html);
        Assert.Contains("data-progress=\"draft-errors\">0", html);
        Assert.Contains("data-progress=\"missing\">1", html);
        Assert.Contains("Valid draft", html);
        Assert.Contains("Clone published 2.49", html);
    }

    [Theory]
    [InlineData("editor", true, false)]
    [InlineData("publisher", false, true)]
    [InlineData("admin", true, true)]
    public async Task Workspace_ShowsLifecycleControlsForTheCorrectRoles(string username, bool canEdit, bool canPublish)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(Options());
        await LoginAsync(client, username);
        var html = await (await client.GetAsync(WorkspacePath)).Content.ReadAsStringAsync();
        Assert.Equal(canEdit, html.Contains("handler=CreateDraft", StringComparison.Ordinal));
        Assert.Equal(canEdit, html.Contains("handler=Clone", StringComparison.Ordinal));
        Assert.Equal(canEdit, html.Contains(">Import</a>", StringComparison.Ordinal));
        Assert.Equal(canPublish, html.Contains("handler=Publish", StringComparison.Ordinal));
        Assert.Contains("Open visual editor", html);
        Assert.Contains("Version diff", html);
    }

    [Fact]
    public async Task InvalidDraft_ShowsErrorCountAndDisablesPublish()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(Options());
        await LoginAsync(client, "publisher");
        factory.MockApiHandler.InvalidNextValidation = true;
        var html = await (await client.GetAsync(WorkspacePath)).Content.ReadAsStringAsync();
        Assert.Contains("data-progress=\"draft-errors\">1", html);
        Assert.Contains("2 validation errors", html);
        Assert.Contains("Prerequisite cycle detected", html);
        Assert.Contains("disabled", html);
        Assert.Contains(">Publish</button>", html);
    }

    [Fact]
    public async Task ConfirmedPublish_TransitionsWorkspaceStatusAndProgress()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(Options());
        await LoginAsync(client, "publisher");
        var page = await client.GetAsync(WorkspacePath);
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);
        var response = await client.PostAsync($"{WorkspacePath}?handler=Publish", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["researchTreeId"] = TestMockApiHandler.ResearchTreeId.ToString(),
            ["versionId"] = TestMockApiHandler.DraftVersionId.ToString(),
            ["confirmImmutable"] = "true",
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var html = await (await client.GetAsync(response.Headers.Location!)).Content.ReadAsStringAsync();
        Assert.Contains("was published as an immutable snapshot", html);
        Assert.Contains("data-progress=\"published\">1", html);
        Assert.Contains("data-progress=\"valid-draft\">0", html);
        Assert.Contains("Published", html);
        Assert.Contains("Inspect tree", html);
        Assert.DoesNotContain("handler=Publish", html);
        Assert.True(factory.MockApiHandler.DraftPublished);
    }

    [Fact]
    public async Task CloneHandler_RejectsSourceFromAnotherTree()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(Options());
        await LoginAsync(client, "editor");
        var page = await client.GetAsync(WorkspacePath);
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);
        factory.MockApiHandler.RecordedRequests.Clear();
        var response = await client.PostAsync($"{WorkspacePath}?handler=Clone", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["researchTreeId"] = TestMockApiHandler.OtherTreeId.ToString(),
            ["sourceVersionId"] = TestMockApiHandler.PublishedVersionId.ToString(),
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(factory.MockApiHandler.RecordedRequests, request => request.RequestUri?.AbsolutePath.EndsWith("/clone") == true);
    }

    private static WebApplicationFactoryClientOptions Options() => new() { AllowAutoRedirect = false, HandleCookies = true };

    private static async Task LoginAsync(HttpClient client, string username)
    {
        var page = await client.GetAsync("/Login");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);
        var response = await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Username"] = username,
            ["Input.Password"] = $"{username}123",
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
