using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GrindingThunder.Admin.Tests;

public class VisualTreeIntegrationTests
{
    [Fact]
    public async Task VisualTreeAssets_KeepGridStepsSynchronizedAndProvideKeyboardNudging()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());

        var css = await client.GetStringAsync("/css/admin.css");
        var javascript = await client.GetStringAsync("/js/visual-tree.js");

        Assert.Contains("--visual-tree-column-step: 280px", css);
        Assert.Contains("--visual-tree-row-step: 204px", css);
        Assert.Contains("background-size: var(--visual-tree-column-step) var(--visual-tree-row-step)", css);
        Assert.Contains("const defaultColumnStep = 280", javascript);
        Assert.Contains("const defaultRowStep = 204", javascript);
        Assert.Contains("[\"ArrowLeft\", \"ArrowRight\", \"ArrowUp\", \"ArrowDown\"]", javascript);
        Assert.Contains("L ${exit.x} ${exit.y} C", javascript);
    }

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
        Assert.Contains("data-tree-column-step=\"280\"", html);
        Assert.Contains("data-tree-row-step=\"204\"", html);
        Assert.Contains("https://example.test/f16.png", html);
        Assert.Contains("data-edge-kind=\"prerequisite\"", html);
        Assert.Contains("data-edge-kind=\"folder\"", html);
        Assert.Contains(">Premium</span>", html);
        Assert.Contains(">Folder</span>", html);
        Assert.Contains(">Folder member</span>", html);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    public async Task DraftEditor_RendersEditModeNodeInspectorAndContextualAddition(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);

        var response = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-tree-edit-toggle", html);
        Assert.Contains("Entry inspector", html);
        Assert.Contains("name=\"VisualEntry.TreeRankId\"", html);
        Assert.Contains("name=\"VisualEntry.RpCost\"", html);
        Assert.Contains("name=\"VisualEntry.TreeColumn\"", html);
        Assert.Contains("data-tree-grid-guide", html);
        Assert.Contains("data-inspector-coordinate", html);
        Assert.Contains("data-tree-coordinate", html);
        Assert.Contains("Column 1 · Row 0", html);
        Assert.Contains("press an arrow key", html);
        Assert.Contains("name=\"VisualEntry.FolderParentEntryId\"", html);
        Assert.Contains("Save Changes", html);
        Assert.Contains("Remove from Draft", html);
        Assert.Contains("data-tree-edge-toggle", html);
        Assert.Contains("data-tree-edge-panel", html);
        Assert.Contains("data-tree-edge-create-form", html);
        Assert.Contains("data-tree-edge-remove-form", html);
        Assert.Contains("name=\"AddPrerequisite.VehicleTreeEntryId\"", html);
        Assert.Contains("name=\"AddPrerequisite.PrerequisiteVehicleTreeEntryId\"", html);
        Assert.Contains("data-vehicle-name=\"F-16A\"", html);
        Assert.Contains($"Tree ID: <code>{TestMockApiHandler.ResearchTreeId}</code>", html);
        Assert.Contains($"value=\"{TestMockApiHandler.AvailableVehicleId}\">F-15C", html);
        Assert.Contains("Create and Add", html);
    }

    [Fact]
    public async Task DraftEditor_InspectorSeparatesStableIdentityAndTracksVersionDataChanges()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, "editor");

        var response = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var html = await response.Content.ReadAsStringAsync();
        var javascript = await client.GetStringAsync("/js/visual-tree.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-inspector-identity", html);
        Assert.Contains("Stable vehicle identity", html);
        Assert.Contains("data-inspector-vehicle-id", html);
        Assert.Contains("data-inspector-version-data", html);
        Assert.Contains("Version-specific entry data", html);
        Assert.Contains("data-dirty-track=\"vehicle-entry\"", html);
        Assert.Contains("data-inspector-dirty", html);
        Assert.Contains("Unsaved changes", html);
        Assert.Contains("data-dirty-field=\"treeColumn\"", html);
        Assert.Contains("data-dirty-field=\"treeRow\"", html);
        Assert.Contains("data-tree-previous", html);
        Assert.Contains("data-tree-next", html);
        Assert.Contains("confirmDiscardChanges", javascript);
        Assert.Contains("beforeunload", javascript);
        Assert.Contains("previewInspectorState", javascript);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    public async Task VisualInspector_UpdatePersistsConfigurationAndCoordinates(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);
        var page = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=UpdateVisualEntry",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["VisualEntry.EntryId"] = TestMockApiHandler.EntryId2.ToString(),
                ["VisualEntry.TreeRankId"] = TestMockApiHandler.SparseDraftRankId.ToString(),
                ["VisualEntry.RpCost"] = "190000",
                ["VisualEntry.SlCost"] = "310000",
                ["VisualEntry.TreeColumn"] = "4",
                ["VisualEntry.TreeRow"] = "3",
                ["VisualEntry.AcquisitionType"] = "2",
                ["VisualEntry.FolderParentEntryId"] = TestMockApiHandler.EntryId.ToString(),
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var request = factory.MockApiHandler.RecordedJsonRequests.Single(recorded =>
            recorded.Path == $"/api/admin/drafts/{TestMockApiHandler.DraftVersionId}/entries/{TestMockApiHandler.EntryId2}");
        using var payload = JsonDocument.Parse(request.Body);
        Assert.Equal(190000, payload.RootElement.GetProperty("RpCost").GetInt32());
        Assert.Equal(310000, payload.RootElement.GetProperty("SlCost").GetInt32());
        Assert.Equal(4, payload.RootElement.GetProperty("TreeColumn").GetInt32());
        Assert.Equal(3, payload.RootElement.GetProperty("TreeRow").GetInt32());
        Assert.Equal(2, payload.RootElement.GetProperty("AcquisitionType").GetInt32());
        Assert.Equal(TestMockApiHandler.SparseDraftRankId, payload.RootElement.GetProperty("TreeRankId").GetGuid());
        Assert.Equal(TestMockApiHandler.EntryId, payload.RootElement.GetProperty("FolderParentEntryId").GetGuid());
    }

    [Fact]
    public async Task VisualWorkspace_AddsExistingTreeScopedVehicleNotAlreadyInDraft()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, "editor");
        var page = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=AddEntry",
            EntryForm("AddEntry", TestMockApiHandler.AvailableVehicleId, token));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var request = factory.MockApiHandler.RecordedJsonRequests.Single(recorded =>
            recorded.Path == $"/api/admin/drafts/{TestMockApiHandler.DraftVersionId}/entries");
        using var payload = JsonDocument.Parse(request.Body);
        Assert.Equal(TestMockApiHandler.AvailableVehicleId, payload.RootElement.GetProperty("VehicleId").GetGuid());
        Assert.Equal(2, payload.RootElement.GetProperty("TreeColumn").GetInt32());
    }

    [Fact]
    public async Task VisualWorkspace_CreatesVehicleInOwningTreeThenAddsItToDraft()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, "admin");
        var page = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=CreateAndAddEntry",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["CreateEntry.Name"] = "F-14B",
                ["CreateEntry.ImageUrl"] = "https://example.test/f14.png",
                ["CreateEntry.TreeRankId"] = TestMockApiHandler.RankId.ToString(),
                ["CreateEntry.RpCost"] = "200000",
                ["CreateEntry.SlCost"] = "320000",
                ["CreateEntry.TreeColumn"] = "3",
                ["CreateEntry.TreeRow"] = "1",
                ["CreateEntry.AcquisitionType"] = "0",
                ["CreateEntry.FolderParentEntryId"] = "",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var create = factory.MockApiHandler.RecordedJsonRequests.Single(recorded => recorded.Path == "/api/admin/vehicles");
        using (var payload = JsonDocument.Parse(create.Body))
        {
            Assert.Equal(TestMockApiHandler.ResearchTreeId, payload.RootElement.GetProperty("ResearchTreeId").GetGuid());
            Assert.Equal("F-14B", payload.RootElement.GetProperty("Name").GetString());
        }
        var add = factory.MockApiHandler.RecordedJsonRequests.Single(recorded =>
            recorded.Path == $"/api/admin/drafts/{TestMockApiHandler.DraftVersionId}/entries");
        using (var payload = JsonDocument.Parse(add.Body))
        {
            Assert.Equal(TestMockApiHandler.CreatedVehicleId, payload.RootElement.GetProperty("VehicleId").GetGuid());
            Assert.Equal(3, payload.RootElement.GetProperty("TreeColumn").GetInt32());
        }
    }

    [Fact]
    public async Task VisualInspector_ApiValidationErrorIsDisplayedInWorkspace()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, "editor");
        var page = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);
        factory.MockApiHandler.ConflictNextEntryUpdate = true;

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=UpdateVisualEntry",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["VisualEntry.EntryId"] = TestMockApiHandler.EntryId.ToString(),
                ["VisualEntry.TreeRankId"] = TestMockApiHandler.RankId.ToString(),
                ["VisualEntry.RpCost"] = "220000",
                ["VisualEntry.SlCost"] = "350000",
                ["VisualEntry.TreeColumn"] = "1",
                ["VisualEntry.TreeRow"] = "0",
                ["VisualEntry.AcquisitionType"] = "0",
                ["__RequestVerificationToken"] = token
            }));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Coordinate collision: another root entry already occupies that tree position.", html);
        Assert.Contains("data-tree-edit-toggle", html);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    public async Task VisualPrerequisiteForm_CreatesEdgeThroughExistingHandler(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);
        var page = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=AddPrerequisite",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["AddPrerequisite.VehicleTreeEntryId"] = TestMockApiHandler.EntryId.ToString(),
                ["AddPrerequisite.PrerequisiteVehicleTreeEntryId"] = TestMockApiHandler.EntryId2.ToString(),
                ["VisualEdgeMode"] = "true",
                ["VisualEdgeSelection"] = "false",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var request = factory.MockApiHandler.RecordedJsonRequests.Single(recorded =>
            recorded.Path == $"/api/admin/drafts/{TestMockApiHandler.DraftVersionId}/prerequisites");
        using var payload = JsonDocument.Parse(request.Body);
        Assert.Equal(TestMockApiHandler.EntryId, payload.RootElement.GetProperty("VehicleTreeEntryId").GetGuid());
        Assert.Equal(TestMockApiHandler.EntryId2, payload.RootElement.GetProperty("PrerequisiteVehicleTreeEntryId").GetGuid());
    }

    [Fact]
    public async Task VisualPrerequisiteForm_RemovesSelectedEdgeThroughExistingHandler()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, "editor");
        var page = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=RemovePrerequisite",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["entryId"] = TestMockApiHandler.EntryId2.ToString(),
                ["prerequisiteEntryId"] = TestMockApiHandler.EntryId.ToString(),
                ["AddPrerequisite.VehicleTreeEntryId"] = TestMockApiHandler.EntryId2.ToString(),
                ["AddPrerequisite.PrerequisiteVehicleTreeEntryId"] = TestMockApiHandler.EntryId.ToString(),
                ["VisualEdgeMode"] = "true",
                ["VisualEdgeSelection"] = "true",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.Method == HttpMethod.Delete &&
            request.RequestUri?.AbsolutePath ==
                $"/api/admin/drafts/{TestMockApiHandler.DraftVersionId}/prerequisites/{TestMockApiHandler.EntryId2}/{TestMockApiHandler.EntryId}");
    }

    [Theory]
    [InlineData(false, "An entry cannot be a prerequisite of itself.")]
    [InlineData(true, "Prerequisite edge already exists in this draft version.")]
    public async Task VisualPrerequisiteForm_ApiErrorIsDisplayedInsideWorkspaceAndPreservesEdgeMode(
        bool duplicate,
        string expectedError)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, "editor");
        var page = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);
        var dependentId = duplicate ? TestMockApiHandler.EntryId2 : TestMockApiHandler.EntryId;

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=AddPrerequisite",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["AddPrerequisite.VehicleTreeEntryId"] = dependentId.ToString(),
                ["AddPrerequisite.PrerequisiteVehicleTreeEntryId"] = TestMockApiHandler.EntryId.ToString(),
                ["VisualEdgeMode"] = "true",
                ["__RequestVerificationToken"] = token
            }));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-tree-workspace-alert", html);
        Assert.Contains(expectedError, html);
        Assert.Contains("data-start-edge-mode=\"true\"", html);
        Assert.Contains($"data-pending-dependent=\"{dependentId}\"", html);
        Assert.Contains($"data-pending-prerequisite=\"{TestMockApiHandler.EntryId}\"", html);
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
        Assert.DoesNotContain("data-tree-edit-toggle", html);
        Assert.DoesNotContain("data-tree-edge-toggle", html);
        Assert.DoesNotContain("data-tree-edge-remove-form", html);
        Assert.DoesNotContain("Entry inspector", html);
        Assert.DoesNotContain("data-tree-inspector-form", html);
        Assert.DoesNotContain("data-inspector-dirty", html);
        Assert.DoesNotContain("data-dirty-field", html);
        Assert.DoesNotContain("Save Changes", html);
        Assert.DoesNotContain("Create and Add", html);
        Assert.DoesNotContain("handler=Publish", html);
        if (username == "publisher")
        {
            Assert.DoesNotContain(factory.MockApiHandler.RecordedRequests, request =>
                request.RequestUri?.AbsolutePath.StartsWith("/api/admin/", StringComparison.OrdinalIgnoreCase) == true);
        }
    }

    [Fact]
    public async Task PublisherDraftVisualTree_IsReadOnlyAndDoesNotCallEditorEndpoints()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, "publisher");
        factory.MockApiHandler.RecordedRequests.Clear();

        var response = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-visual-tree", html);
        Assert.DoesNotContain("data-tree-edit-toggle", html);
        Assert.DoesNotContain("data-tree-edge-toggle", html);
        Assert.DoesNotContain("data-tree-edge-remove-form", html);
        Assert.DoesNotContain("Entry inspector", html);
        Assert.DoesNotContain("data-tree-inspector-form", html);
        Assert.DoesNotContain("data-inspector-dirty", html);
        Assert.DoesNotContain("data-dirty-field", html);
        Assert.DoesNotContain(factory.MockApiHandler.RecordedRequests, request =>
            request.RequestUri?.AbsolutePath.StartsWith("/api/admin/", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static FormUrlEncodedContent EntryForm(string prefix, Guid vehicleId, string token) => new(
        new Dictionary<string, string>
        {
            [$"{prefix}.VehicleId"] = vehicleId.ToString(),
            [$"{prefix}.TreeRankId"] = TestMockApiHandler.RankId.ToString(),
            [$"{prefix}.RpCost"] = "150000",
            [$"{prefix}.SlCost"] = "240000",
            [$"{prefix}.TreeColumn"] = "2",
            [$"{prefix}.TreeRow"] = "1",
            [$"{prefix}.AcquisitionType"] = "0",
            [$"{prefix}.FolderParentEntryId"] = "",
            ["__RequestVerificationToken"] = token
        });

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
