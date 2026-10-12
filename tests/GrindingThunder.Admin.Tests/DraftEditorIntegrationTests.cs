using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GrindingThunder.Admin.Tests;

public class DraftEditorIntegrationTests
{
    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    public async Task DraftVersionDetail_RendersRanksEntriesPrerequisitesAndEditorForms(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);

        var response = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Draft", html);
        Assert.Contains("2.50", html);
        Assert.Contains("Rank 1", html);
        Assert.Contains("F-16A", html);
        Assert.Contains("F-4E", html);
        Assert.Contains("handler=AddRank", html);
        Assert.Contains("handler=UpdateRank", html);
        Assert.Contains("handler=AddEntry", html);
        Assert.Contains("handler=AddPrerequisite", html);
        Assert.Contains("Input.RpCost", await (await client.GetAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}/Entry/{TestMockApiHandler.EntryId}")).Content.ReadAsStringAsync());
        foreach (var handler in new[] { "DeleteRank", "DeleteEntry", "RemovePrerequisite" })
        {
            var forms = Regex.Matches(html, "<form\\b[^>]*>.*?</form>", RegexOptions.Singleline)
                .Select(match => match.Value)
                .Where(form => form.Contains($"handler={handler}"))
                .ToList();
            Assert.NotEmpty(forms);
            Assert.All(forms, form => Assert.Contains("onsubmit=\"return confirm('", form));
        }
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    [InlineData("publisher")]
    public async Task PublishedVersionDetail_RendersReadOnlyViewWithoutMutationForms(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);

        var response = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.PublishedVersionId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Published", html);
        Assert.Contains("immutable", html);
        Assert.Contains("F-16A", html);
        Assert.Contains("F-4E", html);
        Assert.Contains("Rank 1", html);
        Assert.DoesNotContain("Other snapshot entry", html);
        Assert.DoesNotContain("987654", html);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.RequestUri?.AbsolutePath == "/api/vehicles/tree" &&
            request.RequestUri.Query.Contains(TestMockApiHandler.NationId.ToString()) &&
            request.RequestUri.Query.Contains("Aviation"));
        foreach (var handler in new[] { "AddRank", "UpdateRank", "DeleteRank", "AddEntry", "DeleteEntry", "AddPrerequisite", "RemovePrerequisite" })
            Assert.DoesNotContain($"handler={handler}", html);
        Assert.DoesNotContain($"/Entry/{TestMockApiHandler.EntryId}", html);

        if (username == "publisher")
        {
            Assert.DoesNotContain(factory.MockApiHandler.RecordedRequests, request =>
                request.RequestUri?.AbsolutePath.StartsWith("/api/admin/") == true);
        }
        else
        {
            Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
                request.RequestUri?.AbsolutePath == $"/api/admin/research-tree-versions/{TestMockApiHandler.PublishedVersionId}");
        }
    }

    [Fact]
    public async Task Publisher_DraftVersionDetail_RendersReadOnlyView_WithoutCallingEditorEndpoints()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, "publisher");
        factory.MockApiHandler.RecordedRequests.Clear();

        var response = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Draft", html);
        Assert.Contains("2.50", html);
        Assert.Contains("Rank 1", html);
        Assert.Contains("Other snapshot entry", html);
        Assert.Contains("987654", html);
        Assert.DoesNotContain("F-16A", html);
        Assert.DoesNotContain("F-4E", html);
        foreach (var handler in new[] { "AddRank", "UpdateRank", "DeleteRank", "AddEntry", "DeleteEntry", "AddPrerequisite", "RemovePrerequisite" })
            Assert.DoesNotContain($"handler={handler}", html);
        Assert.DoesNotContain($"/Entry/{TestMockApiHandler.EntryId}", html);

        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.RequestUri?.AbsolutePath == "/api/nations");
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.RequestUri?.AbsolutePath == "/api/vehicles/tree");
        Assert.DoesNotContain(factory.MockApiHandler.RecordedRequests, request =>
            request.RequestUri?.AbsolutePath.StartsWith("/api/admin/") == true);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    public async Task EditorAndAdmin_DraftVersionDetail_UsesSecuredAdminEndpoints(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);
        factory.MockApiHandler.RecordedRequests.Clear();

        var response = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.RequestUri?.AbsolutePath == $"/api/admin/research-tree-versions/{TestMockApiHandler.DraftVersionId}");
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.RequestUri?.AbsolutePath == $"/api/admin/research-trees/{TestMockApiHandler.ResearchTreeId}");
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.RequestUri?.AbsolutePath == $"/api/admin/drafts/{TestMockApiHandler.DraftVersionId}");
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    [InlineData("publisher")]
    public async Task VersionDetail_UnknownVersionId_ReturnsNotFound_DoesNotFallBack(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);

        var unknownId = Guid.NewGuid();
        var response = await client.GetAsync($"/Trees/Version/{unknownId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("was not found.", html);
        Assert.Contains(unknownId.ToString(), html);
        Assert.DoesNotContain("F-16A", html);
        Assert.DoesNotContain("Other snapshot entry", html);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    public async Task AddRank_InvokesDraftApiAndRedirects(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);
        var page = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=AddRank",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["AddRank.RankNumber"] = "2",
                ["AddRank.RequiredVehiclesUnlocked"] = "4",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.Method == HttpMethod.Post &&
            request.RequestUri?.AbsolutePath == $"/api/admin/drafts/{TestMockApiHandler.DraftVersionId}/ranks");
    }

    [Fact]
    public async Task AddRank_WhenApiConflicts_RendersErrorMessage()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client);
        var page = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);
        factory.MockApiHandler.ConflictNextCatalogMutation = true;

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=AddRank",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["AddRank.RankNumber"] = "2",
                ["AddRank.RequiredVehiclesUnlocked"] = "4",
                ["__RequestVerificationToken"] = token
            }));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("A matching catalog item already exists.", html);
    }

    [Fact]
    public async Task AddRank_WithInvalidRankNumber_ShowsApiValidationError()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client);
        var page = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=AddRank",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["AddRank.RankNumber"] = "0",
                ["AddRank.RequiredVehiclesUnlocked"] = "4",
                ["__RequestVerificationToken"] = token
            }));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Rank number must be positive.", html);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    public async Task UpdateRank_InvokesDraftApi(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);
        var page = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=UpdateRank",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["rankId"] = TestMockApiHandler.RankId.ToString(),
                ["requiredVehiclesUnlocked"] = "6",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.Method == HttpMethod.Put &&
            request.RequestUri?.AbsolutePath == $"/api/admin/drafts/{TestMockApiHandler.DraftVersionId}/ranks/{TestMockApiHandler.RankId}");
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    public async Task DeleteRank_InvokesDraftApi(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);
        var page = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=DeleteRank",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["rankId"] = TestMockApiHandler.RankId.ToString(),
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.Method == HttpMethod.Delete &&
            request.RequestUri?.AbsolutePath == $"/api/admin/drafts/{TestMockApiHandler.DraftVersionId}/ranks/{TestMockApiHandler.RankId}");
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    public async Task AddEntry_InvokesDraftApiWithCoordinatesAndAcquisitionType(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);
        var page = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=AddEntry",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["AddEntry.VehicleId"] = TestMockApiHandler.VehicleId2.ToString(),
                ["AddEntry.TreeRankId"] = TestMockApiHandler.RankId.ToString(),
                ["AddEntry.RpCost"] = "150000",
                ["AddEntry.SlCost"] = "200000",
                ["AddEntry.TreeColumn"] = "2",
                ["AddEntry.TreeRow"] = "1",
                ["AddEntry.AcquisitionType"] = "1",
                ["AddEntry.FolderParentEntryId"] = "",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var recorded = factory.MockApiHandler.RecordedRequests.Single(request =>
            request.Method == HttpMethod.Post &&
            request.RequestUri?.AbsolutePath == $"/api/admin/drafts/{TestMockApiHandler.DraftVersionId}/entries");
        var body = await recorded.Content!.ReadAsStringAsync();
        Assert.Contains(TestMockApiHandler.VehicleId2.ToString(), body);
        Assert.Contains("150000", body);
        Assert.Contains("\"AcquisitionType\":1", body);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    public async Task DeleteEntry_InvokesDraftApi(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);
        var page = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=DeleteEntry",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["entryId"] = TestMockApiHandler.EntryId.ToString(),
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.Method == HttpMethod.Delete &&
            request.RequestUri?.AbsolutePath == $"/api/admin/drafts/{TestMockApiHandler.DraftVersionId}/entries/{TestMockApiHandler.EntryId}");
    }

    [Fact]
    public async Task AddPrerequisite_SelfEdge_ShowsApiValidationError()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client);
        var page = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=AddPrerequisite",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["AddPrerequisite.VehicleTreeEntryId"] = TestMockApiHandler.EntryId.ToString(),
                ["AddPrerequisite.PrerequisiteVehicleTreeEntryId"] = TestMockApiHandler.EntryId.ToString(),
                ["__RequestVerificationToken"] = token
            }));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("An entry cannot be a prerequisite of itself.", html);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    public async Task RemovePrerequisite_InvokesDraftApi(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);
        var page = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=RemovePrerequisite",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["entryId"] = TestMockApiHandler.EntryId2.ToString(),
                ["prerequisiteEntryId"] = TestMockApiHandler.EntryId.ToString(),
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request =>
            request.Method == HttpMethod.Delete &&
            request.RequestUri?.AbsolutePath == $"/api/admin/drafts/{TestMockApiHandler.DraftVersionId}/prerequisites/{TestMockApiHandler.EntryId2}/{TestMockApiHandler.EntryId}");
    }

    [Theory]
    [InlineData("AddRank")]
    [InlineData("UpdateRank")]
    [InlineData("DeleteRank")]
    [InlineData("AddEntry")]
    [InlineData("DeleteEntry")]
    [InlineData("AddPrerequisite")]
    [InlineData("RemovePrerequisite")]
    public async Task DraftMutationOnPublishedVersion_BlockedBeforeReachingApi(string handler)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client);
        // The published detail page renders no forms, so the antiforgery token
        // is taken from the index page (tokens are per-session, not per-page).
        var form = await client.GetAsync("/Trees");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(form);
        factory.MockApiHandler.RecordedRequests.Clear();

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.PublishedVersionId}?handler={handler}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["AddRank.RankNumber"] = "2",
                ["AddRank.RequiredVehiclesUnlocked"] = "4",
                ["__RequestVerificationToken"] = token
            }));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Published versions are immutable and cannot be edited.", html);
        Assert.DoesNotContain(factory.MockApiHandler.RecordedRequests, request =>
            request.RequestUri?.AbsolutePath.Contains("/api/admin/drafts/", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    public async Task EntryEditPage_LoadsEntryAndUpdatesThroughDraftApi(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);
        var path = $"/Trees/Version/{TestMockApiHandler.DraftVersionId}/Entry/{TestMockApiHandler.EntryId2}";

        var form = await client.GetAsync(path);
        var formHtml = await form.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        Assert.Contains("F-4E", formHtml);

        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(form);
        var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.TreeRankId"] = TestMockApiHandler.RankId.ToString(),
            ["Input.RpCost"] = "185000",
            ["Input.SlCost"] = "295000",
            ["Input.TreeColumn"] = "3",
            ["Input.TreeRow"] = "2",
            ["Input.AcquisitionType"] = "0",
            ["Input.FolderParentEntryId"] = TestMockApiHandler.EntryId.ToString(),
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var recorded = factory.MockApiHandler.RecordedRequests.Single(request =>
            request.Method == HttpMethod.Put &&
            request.RequestUri?.AbsolutePath == $"/api/admin/drafts/{TestMockApiHandler.DraftVersionId}/entries/{TestMockApiHandler.EntryId2}");
        var body = await recorded.Content!.ReadAsStringAsync();
        Assert.Contains("185000", body);
        Assert.Contains("\"TreeColumn\":3", body);
        Assert.Contains("\"TreeRow\":2", body);
        using var payload = JsonDocument.Parse(body);
        Assert.Equal(TestMockApiHandler.RankId, payload.RootElement.GetProperty("TreeRankId").GetGuid());
        Assert.Equal(TestMockApiHandler.EntryId, payload.RootElement.GetProperty("FolderParentEntryId").GetGuid());
    }

    [Fact]
    public async Task EntryEditPage_ForPublishedVersion_ShowsImmutableMessage()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client);

        var response = await client.GetAsync(
            $"/Trees/Version/{TestMockApiHandler.PublishedVersionId}/Entry/{TestMockApiHandler.EntryId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Published versions are immutable.", html);
        Assert.DoesNotContain("Input.RpCost", html);
        Assert.DoesNotContain("Input.FolderParentEntryId", html);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    public async Task AddPrerequisite_InvokesDraftApi(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);
        var page = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);
        var response = await client.PostAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=AddPrerequisite",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["AddPrerequisite.VehicleTreeEntryId"] = TestMockApiHandler.EntryId.ToString(),
                ["AddPrerequisite.PrerequisiteVehicleTreeEntryId"] = TestMockApiHandler.EntryId2.ToString(),
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request => request.Method == HttpMethod.Post &&
            request.RequestUri?.AbsolutePath == $"/api/admin/drafts/{TestMockApiHandler.DraftVersionId}/prerequisites");
    }

    [Theory]
    [InlineData("AddRank")]
    [InlineData("UpdateRank")]
    [InlineData("DeleteRank")]
    [InlineData("AddEntry")]
    [InlineData("DeleteEntry")]
    [InlineData("AddPrerequisite")]
    [InlineData("RemovePrerequisite")]
    public async Task Publisher_CannotSeeOrInvokeDraftMutationForms(string handler)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, "publisher");
        var page = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var html = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.DoesNotContain($"handler={handler}", html);
        Assert.DoesNotContain($"/Entry/{TestMockApiHandler.EntryId}", html);
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);
        Assert.NotEmpty(token);
        factory.MockApiHandler.RecordedRequests.Clear();

        var response = await client.PostAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler={handler}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/AccessDenied", response.Headers.Location?.OriginalString);
        Assert.DoesNotContain(factory.MockApiHandler.RecordedRequests, request => request.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task Publisher_CannotGetOrPostEntryEditPage()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, "publisher");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(await client.GetAsync("/Trees"));
        factory.MockApiHandler.RecordedRequests.Clear();
        var path = $"/Trees/Version/{TestMockApiHandler.DraftVersionId}/Entry/{TestMockApiHandler.EntryId}";

        var get = await client.GetAsync(path);
        var post = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.TreeRankId"] = TestMockApiHandler.RankId.ToString(),
            ["Input.RpCost"] = "1234"
        }));

        foreach (var response in new[] { get, post })
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var location = response.Headers.Location!;
            Assert.StartsWith("/AccessDenied", location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString);
        }
        Assert.Empty(factory.MockApiHandler.RecordedRequests);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    public async Task EntryEditPost_ForPublishedVersion_BlockedBeforeMutationApi(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client, username);
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(await client.GetAsync("/Trees"));
        factory.MockApiHandler.RecordedRequests.Clear();

        var response = await client.PostAsync(
            $"/Trees/Version/{TestMockApiHandler.PublishedVersionId}/Entry/{TestMockApiHandler.EntryId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Input.TreeRankId"] = TestMockApiHandler.Rank2Id.ToString(),
                ["Input.RpCost"] = "1234"
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Published versions are immutable.", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(factory.MockApiHandler.RecordedRequests, request => request.Method != HttpMethod.Get);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EntryEdit_UnknownEntry_IsScopedToSelectedDraft(bool post)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client);
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(await client.GetAsync("/Trees"));
        factory.MockApiHandler.RecordedRequests.Clear();
        var path = $"/Trees/Version/{TestMockApiHandler.DraftVersionId}/Entry/{Guid.NewGuid()}";
        var response = post
            ? await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }))
            : await client.GetAsync(path);

        Assert.Contains("was not found in this draft version.", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(factory.MockApiHandler.RecordedRequests, request => request.Method != HttpMethod.Get);
    }

    [Theory]
    [InlineData("/Trees")]
    [InlineData("/Trees/Version/66666666-6666-6666-6666-666666666666")]
    [InlineData("/Trees/Version/66666666-6666-6666-6666-666666666666/Entry/99999999-9999-9999-9999-999999999999")]
    public async Task TechTreesNavigation_IsActiveOnEditorRoutes(string path)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(ClientOptions());
        await LoginAsync(client);
        var html = await (await client.GetAsync(path)).Content.ReadAsStringAsync();
        Assert.Matches("<li class=\"nav-item active\"><a class=\"nav-link\" href=\"/Trees\">Tech Trees</a></li>", html);
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
