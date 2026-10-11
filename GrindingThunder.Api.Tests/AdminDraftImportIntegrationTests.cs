using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class AdminDraftImportIntegrationTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminDraftImportIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.InitializeDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string> GetEditorTokenAsync() => await _factory.GetTokenAsync("editor", "EditorPass123!");
    private async Task<string> GetPublisherTokenAsync() => await _factory.GetTokenAsync("publisher", "PublisherPass123!");

    private ApplicationDbContext CreateDbContext()
    {
        var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    private static MultipartFormDataContent CreateMultipartFileContent(string csvContent, string fileName = "tree.csv")
    {
        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csvContent));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        multipart.Add(fileContent, "file", fileName);
        return multipart;
    }

    [Fact]
    public async Task ImportEndpoints_UnauthorizedWithoutToken()
    {
        var multipartPreview = CreateMultipartFileContent("dummy");
        var previewResp = await _client.PostAsync($"/api/admin/drafts/{Guid.NewGuid()}/import/preview", multipartPreview);
        Assert.Equal(HttpStatusCode.Unauthorized, previewResp.StatusCode);

        var multipartImport = CreateMultipartFileContent("dummy");
        var importResp = await _client.PostAsync($"/api/admin/drafts/{Guid.NewGuid()}/import", multipartImport);
        Assert.Equal(HttpStatusCode.Unauthorized, importResp.StatusCode);
    }

    [Fact]
    public async Task ImportEndpoints_PublisherForbidden_EditorAllowed()
    {
        using var db = CreateDbContext();
        var tree = await db.ResearchTrees.FirstAsync();
        var newUpdate = new GameUpdate { Id = Guid.NewGuid(), Version = "2.97" };
        db.GameUpdates.Add(newUpdate);
        var draft = new ResearchTreeVersion
        {
            Id = Guid.NewGuid(),
            ResearchTreeId = tree.Id,
            GameUpdateId = newUpdate.Id,
            Status = ResearchTreeVersionStatus.Draft
        };
        db.ResearchTreeVersions.Add(draft);
        await db.SaveChangesAsync();

        var publisherToken = await GetPublisherTokenAsync();
        var editorToken = await GetEditorTokenAsync();

        var csv = """
            VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow
            Test Vehicle,1,1000,1000,1,1
            """;

        // 1. Publisher -> 403 Forbidden
        using var pubReq = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/drafts/{draft.Id}/import/preview")
        {
            Content = CreateMultipartFileContent(csv)
        };
        pubReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", publisherToken);
        var pubResp = await _client.SendAsync(pubReq);
        Assert.Equal(HttpStatusCode.Forbidden, pubResp.StatusCode);

        // 2. Editor -> 200 OK
        using var editReq = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/drafts/{draft.Id}/import/preview")
        {
            Content = CreateMultipartFileContent(csv)
        };
        editReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", editorToken);
        var editResp = await _client.SendAsync(editReq);
        Assert.Equal(HttpStatusCode.OK, editResp.StatusCode);
    }

    [Fact]
    public async Task ImportEndpoints_MissingDraftVersion_ReturnsNotFound()
    {
        var editorToken = await GetEditorTokenAsync();
        var nonExistentId = Guid.NewGuid();

        var csv = """
            VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow
            Test Vehicle,1,1000,1000,1,1
            """;

        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/drafts/{nonExistentId}/import/preview")
        {
            Content = CreateMultipartFileContent(csv)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", editorToken);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task ImportEndpoints_PublishedVersion_ReturnsConflict()
    {
        using var db = CreateDbContext();
        var publishedVersion = await db.ResearchTreeVersions
            .FirstOrDefaultAsync(v => v.Status == ResearchTreeVersionStatus.Published);

        Assert.NotNull(publishedVersion);

        var editorToken = await GetEditorTokenAsync();
        var csv = """
            VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow
            Test Vehicle,1,1000,1000,1,1
            """;

        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/drafts/{publishedVersion.Id}/import")
        {
            Content = CreateMultipartFileContent(csv)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", editorToken);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task ImportPreviewAndCommit_FullFlow_SucceedsAndPersistsEntities()
    {
        using var db = CreateDbContext();
        var tree = await db.ResearchTrees.FirstAsync();
        var newUpdate = new GameUpdate { Id = Guid.NewGuid(), Version = "2.99" };
        db.GameUpdates.Add(newUpdate);

        var draft = new ResearchTreeVersion
        {
            Id = Guid.NewGuid(),
            ResearchTreeId = tree.Id,
            GameUpdateId = newUpdate.Id,
            Status = ResearchTreeVersionStatus.Draft
        };
        db.ResearchTreeVersions.Add(draft);
        await db.SaveChangesAsync();

        var editorToken = await GetEditorTokenAsync();

        var csv = """
            VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow,AcquisitionType,FolderParentVehicleName,PrerequisiteVehicleNames,RequiredVehiclesUnlocked
            Alpha One,1,5000,15000,1,1,Researchable,,,
            Alpha Two,1,5000,15000,1,2,Researchable,Alpha One,,
            Beta One,2,12000,40000,1,3,Researchable,,Alpha One,1
            """;

        // Step 1: Preview
        using var previewReq = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/drafts/{draft.Id}/import/preview")
        {
            Content = CreateMultipartFileContent(csv)
        };
        previewReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", editorToken);
        var previewResp = await _client.SendAsync(previewReq);
        Assert.Equal(HttpStatusCode.OK, previewResp.StatusCode);

        var previewDto = await previewResp.Content.ReadFromJsonAsync<TechTreeImportPreviewDto>();
        Assert.NotNull(previewDto);
        Assert.True(previewDto.IsValid);
        Assert.Equal(3, previewDto.TotalRowsDetected);
        Assert.Equal(3, previewDto.NewEntriesCount);
        Assert.Equal(2, previewDto.CreatedRanksCount);

        // Step 2: Commit
        using var commitReq = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/drafts/{draft.Id}/import")
        {
            Content = CreateMultipartFileContent(csv)
        };
        commitReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", editorToken);
        var commitResp = await _client.SendAsync(commitReq);
        Assert.Equal(HttpStatusCode.OK, commitResp.StatusCode);

        var commitResult = await commitResp.Content.ReadFromJsonAsync<TechTreeImportCommitResultDto>();
        Assert.NotNull(commitResult);
        Assert.True(commitResult.Success);
        Assert.Equal(3, commitResult.ImportedEntriesCount);
        Assert.Equal(3, commitResult.NewEntriesCount);

        // Step 3: Verify Persistence
        using var verifyDb = CreateDbContext();
        var entries = await verifyDb.VehicleTreeEntries
            .Include(e => e.Vehicle)
            .Where(e => e.ResearchTreeVersionId == draft.Id)
            .ToListAsync();
        Assert.Equal(3, entries.Count);

        var prereqs = await verifyDb.VehiclePrerequisites
            .Where(p => p.ResearchTreeVersionId == draft.Id)
            .ToListAsync();
        Assert.Single(prereqs);
    }

    [Fact]
    public async Task ImportCommit_InvalidCsv_ReturnsBadRequestWithValidationErrors()
    {
        using var db = CreateDbContext();
        var tree = await db.ResearchTrees.FirstAsync();
        var newUpdate = new GameUpdate { Id = Guid.NewGuid(), Version = "2.98" };
        db.GameUpdates.Add(newUpdate);

        var draft = new ResearchTreeVersion
        {
            Id = Guid.NewGuid(),
            ResearchTreeId = tree.Id,
            GameUpdateId = newUpdate.Id,
            Status = ResearchTreeVersionStatus.Draft
        };
        db.ResearchTreeVersions.Add(draft);
        await db.SaveChangesAsync();

        var editorToken = await GetEditorTokenAsync();

        // Cyclic prerequisite CSV
        var invalidCsv = """
            VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow,PrerequisiteVehicleNames
            Plane A,1,1000,1000,1,1,Plane B
            Plane B,1,1000,1000,1,2,Plane A
            """;

        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/drafts/{draft.Id}/import")
        {
            Content = CreateMultipartFileContent(invalidCsv)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", editorToken);
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var errorResult = await resp.Content.ReadFromJsonAsync<TechTreeImportCommitResultDto>();
        Assert.NotNull(errorResult);
        Assert.False(errorResult.Success);
        Assert.NotNull(errorResult.Errors);
        Assert.Contains(errorResult.Errors, e => e.Contains("Prerequisite cycle detected"));
    }
}
