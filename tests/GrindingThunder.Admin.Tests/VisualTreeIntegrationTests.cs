using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GrindingThunder.Admin.Tests;

public class VisualTreeIntegrationTests
{
    [Fact]
    public async Task DraftVersion_RendersVisualTreeWithSparseRanksQuotasLayoutAndRelationships()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, "editor");

        var response = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(">Visual Tree</button>", html);
        Assert.Contains(">Table / Details</button>", html);
        Assert.Contains("data-tree-rank=\"1\"", html);
        Assert.Contains("data-tree-rank=\"4\"", html);
        Assert.DoesNotContain("data-tree-rank=\"2\"", html);
        Assert.Contains("Unlock quota: <strong>5</strong>", html);
        Assert.Contains("Unlock quota: <strong>2</strong>", html);
        Assert.Contains($"data-tree-node=\"{TestMockApiHandler.EntryId}\"", html);
        Assert.Contains("data-tree-column=\"1\"", html);
        Assert.Contains("data-tree-row=\"0\"", html);
        Assert.Contains("https://example.test/f16.png", html);
        Assert.Contains("data-edge-kind=\"prerequisite\"", html);
        Assert.Contains("data-edge-kind=\"folder\"", html);
        Assert.Contains(">Premium</span>", html);
        Assert.Contains(">Folder</span>", html);
        Assert.Contains(">Folder member</span>", html);
    }

    [Fact]
    public async Task PublishedVersion_RendersExactPublishedSnapshotAndSparseRanks()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, "admin");

        var response = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.PublishedVersionId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-tree-rank=\"1\"", html);
        Assert.Contains("data-tree-rank=\"6\"", html);
        Assert.Contains("Unlock quota: <strong>4</strong>", html);
        Assert.Contains("Unlock quota: <strong>3</strong>", html);
        Assert.Contains(">F-16A<", html);
        Assert.Contains(">F-4E<", html);
        Assert.DoesNotContain("Other snapshot entry", html);
        Assert.Contains("data-edge-kind=\"prerequisite\"", html);
        Assert.Contains("This version is published and immutable.", html);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    [InlineData("publisher")]
    public async Task PublishedVisualTree_IsReadOnlyForEveryRole(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);
        factory.MockApiHandler.RecordedRequests.Clear();

        var response = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.PublishedVersionId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-visual-tree", html);
        Assert.DoesNotContain("Add vehicle entry", html);
        Assert.DoesNotContain("Add prerequisite", html);
        Assert.DoesNotContain("handler=UpdateRank", html);
        Assert.DoesNotContain("handler=DeleteRank", html);
        Assert.DoesNotContain("handler=DeleteEntry", html);
        Assert.DoesNotContain("handler=Publish", html);
        if (username == "publisher")
        {
            Assert.DoesNotContain(factory.MockApiHandler.RecordedRequests, request =>
                request.RequestUri?.AbsolutePath.StartsWith("/api/admin/", StringComparison.OrdinalIgnoreCase) == true);
        }
    }

    private static WebApplicationFactoryClientOptions ClientOptions() => new()
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    };

    private static async Task LoginAsync(HttpClient client, string username)
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
