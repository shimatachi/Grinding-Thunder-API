using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GrindingThunder.Admin.Tests;

public class AsyncVisualEditorIntegrationTests
{
    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    public async Task UpdateVisualEntryJson_ValidEditorSave_ReturnsAuthoritativeEntry(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);
        var token = await DraftTokenAsync(client);

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=UpdateVisualEntryJson",
            VisualEntryForm(token, rpCost: 225000, treeColumn: 2, treeRow: 3));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(payload.RootElement.GetProperty("success").GetBoolean());
        var entry = payload.RootElement.GetProperty("entry");
        Assert.Equal(TestMockApiHandler.EntryId, entry.GetProperty("id").GetGuid());
        Assert.Equal(225000, entry.GetProperty("rpCost").GetInt32());
        Assert.Equal(2, entry.GetProperty("treeColumn").GetInt32());
        Assert.Equal(3, entry.GetProperty("treeRow").GetInt32());
        Assert.Equal("Rank 1", entry.GetProperty("rankName").GetString());
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.Method == HttpMethod.Put &&
            request.RequestUri?.AbsolutePath ==
                $"/api/admin/drafts/{TestMockApiHandler.DraftVersionId}/entries/{TestMockApiHandler.EntryId}");
    }

    [Fact]
    public async Task UpdateVisualEntryJson_WhenApiRejects_ReturnsJsonFailure()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, "editor");
        var token = await DraftTokenAsync(client);
        factory.MockApiHandler.ConflictNextEntryUpdate = true;

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=UpdateVisualEntryJson",
            VisualEntryForm(token, treeColumn: 2, treeRow: 3));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(payload.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("Coordinate collision", payload.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task UpdateVisualEntryJson_Publisher_ReturnsForbiddenWithoutMutation()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, "publisher");
        var token = await DraftTokenAsync(client);
        factory.MockApiHandler.RecordedRequests.Clear();

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=UpdateVisualEntryJson",
            VisualEntryForm(token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.MockApiHandler.RecordedRequests);
    }

    [Fact]
    public async Task UpdateVisualEntryJson_Unauthenticated_ReturnsForbidden()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=UpdateVisualEntryJson",
            new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.MockApiHandler.RecordedRequests);
    }

    [Fact]
    public async Task UpdateVisualEntryJson_WithoutAntiforgeryToken_IsRejected()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, "editor");
        factory.MockApiHandler.RecordedRequests.Clear();

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=UpdateVisualEntryJson",
            VisualEntryForm(token: string.Empty));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.MockApiHandler.RecordedRequests);
    }

    [Fact]
    public async Task UpdateVisualEntryJson_PublishedVersion_ReturnsImmutableFailureWithoutMutation()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, "admin");
        var token = await DraftTokenAsync(client);
        factory.MockApiHandler.RecordedRequests.Clear();

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.PublishedVersionId}?handler=UpdateVisualEntryJson",
            VisualEntryForm(token));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(payload.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("immutable", payload.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(factory.MockApiHandler.RecordedRequests, request => request.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task AsyncLayoutPrerequisiteAndEntryRemovalHandlers_CallExistingApiBoundary()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, "editor");
        var token = await DraftTokenAsync(client);
        factory.MockApiHandler.RecordedRequests.Clear();

        var layout = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=UpdateLayoutJson",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["LayoutPositions[0].EntryId"] = TestMockApiHandler.EntryId.ToString(),
                ["LayoutPositions[0].TreeColumn"] = "2",
                ["LayoutPositions[0].TreeRow"] = "2",
                ["LayoutPositions[1].EntryId"] = TestMockApiHandler.EntryId2.ToString(),
                ["LayoutPositions[1].TreeColumn"] = "3",
                ["LayoutPositions[1].TreeRow"] = "2",
                ["__RequestVerificationToken"] = token
            }));
        var addEdge = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=AddPrerequisiteJson",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["AddPrerequisite.VehicleTreeEntryId"] = TestMockApiHandler.EntryId.ToString(),
                ["AddPrerequisite.PrerequisiteVehicleTreeEntryId"] = TestMockApiHandler.EntryId2.ToString(),
                ["__RequestVerificationToken"] = token
            }));
        var removeEdge = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=RemovePrerequisiteJson",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["entryId"] = TestMockApiHandler.EntryId2.ToString(),
                ["prerequisiteEntryId"] = TestMockApiHandler.EntryId.ToString(),
                ["__RequestVerificationToken"] = token
            }));
        var removeEntry = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=DeleteEntryJson",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["entryId"] = TestMockApiHandler.SquadronEntryId.ToString(),
                ["__RequestVerificationToken"] = token
            }));

        foreach (var response in new[] { layout, addEdge, removeEdge, removeEntry })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(payload.RootElement.GetProperty("success").GetBoolean());
        }

        Assert.Equal(2, factory.MockApiHandler.RecordedRequests.Count(request =>
            request.Method == HttpMethod.Put && request.RequestUri?.AbsolutePath.EndsWith("/coordinates") == true));
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath.EndsWith("/prerequisites") == true);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.Method == HttpMethod.Delete && request.RequestUri?.AbsolutePath.Contains("/prerequisites/") == true);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.Method == HttpMethod.Delete && request.RequestUri?.AbsolutePath.EndsWith($"/entries/{TestMockApiHandler.SquadronEntryId}") == true);
    }

    private static FormUrlEncodedContent VisualEntryForm(
        string token,
        int rpCost = 220000,
        int treeColumn = 2,
        int treeRow = 3) => new(new Dictionary<string, string>
        {
            ["VisualEntry.EntryId"] = TestMockApiHandler.EntryId.ToString(),
            ["VisualEntry.TreeRankId"] = TestMockApiHandler.RankId.ToString(),
            ["VisualEntry.RpCost"] = rpCost.ToString(),
            ["VisualEntry.SlCost"] = "350000",
            ["VisualEntry.TreeColumn"] = treeColumn.ToString(),
            ["VisualEntry.TreeRow"] = treeRow.ToString(),
            ["VisualEntry.AcquisitionType"] = "0",
            ["VisualEntry.FolderParentEntryId"] = "",
            ["__RequestVerificationToken"] = token
        });

    private static async Task<string> DraftTokenAsync(HttpClient client) =>
        await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(
            await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}"));

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
