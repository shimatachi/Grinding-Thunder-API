using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GrindingThunder.Admin.Tests;

public class PublicTreeIntegrationTests
{
    [Fact]
    public async Task AnonymousUser_CanBrowsePublishedTreeWithoutLoginRedirect()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync($"/Tree?nationId={TestMockApiHandler.NationId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("/Login?ReturnUrl", response.Headers.Location?.OriginalString ?? string.Empty);
        Assert.Contains("Published version", html);
        Assert.Contains(TestMockApiHandler.PublishedVersionId.ToString(), html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TestMockApiHandler.DraftVersionId.ToString(), html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">F-16A<", html);
        Assert.Contains("data-tree-readonly-inspector", html);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.RequestUri?.AbsolutePath == "/api/vehicles/tree" &&
            request.RequestUri.Query.Contains($"researchTreeVersionId={TestMockApiHandler.PublishedVersionId}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PublicTree_IsCanonicalReadOnlyViewWithoutMutationControls()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/Tree?nationId={TestMockApiHandler.NationId}" +
            $"&researchTreeId={TestMockApiHandler.ResearchTreeId}" +
            $"&versionId={TestMockApiHandler.PublishedVersionId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-visual-tree", html);
        Assert.Contains("data-tree-zoom-in", html);
        Assert.Contains("data-tree-fit", html);
        Assert.Contains("data-edge-kind=\"prerequisite\"", html);
        Assert.Contains("data-tree-readonly-details", html);
        Assert.DoesNotContain("data-tree-edit-toggle", html);
        Assert.DoesNotContain("data-tree-layout-toolbar", html);
        Assert.DoesNotContain("data-tree-edge-toggle", html);
        Assert.DoesNotContain("method=\"post\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Save Changes", html);
        Assert.DoesNotContain("Remove from Draft", html);
    }
}
