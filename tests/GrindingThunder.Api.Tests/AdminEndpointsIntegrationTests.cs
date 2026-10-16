
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class AdminEndpointsIntegrationTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminEndpointsIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.InitializeDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null)
        {
            request.Content = JsonContent.Create(body);
        }
        return request;
    }

    private async Task<string> GetEditorTokenAsync() => await _factory.GetTokenAsync("editor", "EditorPass123!");
    private async Task<string> GetPublisherTokenAsync() => await _factory.GetTokenAsync("publisher", "PublisherPass123!");
    private async Task<string> GetAdminTokenAsync() => await _factory.GetTokenAsync("admin", "AdminPass123!");

    private ApplicationDbContext CreateDbContext()
    {
        var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    #region Version Lifecycle Tests

    [Fact]
    public async Task VersionLifecycle_UnauthorizedWithoutToken()
    {
        var cloneResp = await _client.PostAsJsonAsync($"/api/admin/research-tree-versions/{Guid.NewGuid()}/clone", new CloneVersionRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, cloneResp.StatusCode);

        var publishResp = await _client.PostAsync($"/api/admin/research-tree-versions/{Guid.NewGuid()}/publish", null);
        Assert.Equal(HttpStatusCode.Unauthorized, publishResp.StatusCode);

        var deleteResp = await _client.DeleteAsync($"/api/admin/research-tree-versions/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, deleteResp.StatusCode);
    }

    [Fact]
    public async Task VersionLifecycle_Clone_PublisherForbidden_EditorAllowed()
    {
        using var db = CreateDbContext();
        var sampleVersion = await db.ResearchTreeVersions.FirstAsync();
        var publisherToken = await GetPublisherTokenAsync();
        var editorToken = await GetEditorTokenAsync();

        // New game update for target
        var targetUpdate = new GameUpdate { Id = Guid.NewGuid(), Version = $"clone-test-{Guid.NewGuid():N}"[..18] };
        db.GameUpdates.Add(targetUpdate);
        await db.SaveChangesAsync();

        // Publisher cannot clone (requires Editor)
        using var pubReq = CreateAuthorizedRequest(HttpMethod.Post, $"/api/admin/research-tree-versions/{sampleVersion.Id}/clone", publisherToken, new CloneVersionRequest(targetUpdate.Id));
        var pubResp = await _client.SendAsync(pubReq);
        Assert.Equal(HttpStatusCode.Forbidden, pubResp.StatusCode);

        // Editor can clone
        using var editReq = CreateAuthorizedRequest(HttpMethod.Post, $"/api/admin/research-tree-versions/{sampleVersion.Id}/clone", editorToken, new CloneVersionRequest(targetUpdate.Id));
        var editResp = await _client.SendAsync(editReq);
        Assert.Equal(HttpStatusCode.Created, editResp.StatusCode);

        var result = await editResp.Content.ReadFromJsonAsync<ClonedVersionResponse>();
        Assert.NotNull(result);
        Assert.Equal("Draft", result.Status);
        Assert.Equal(targetUpdate.Id, result.GameUpdateId);

        // Cloning again for the same update returns 409 Conflict
        using var duplicateReq = CreateAuthorizedRequest(HttpMethod.Post, $"/api/admin/research-tree-versions/{sampleVersion.Id}/clone", editorToken, new CloneVersionRequest(targetUpdate.Id));
        var duplicateResp = await _client.SendAsync(duplicateReq);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResp.StatusCode);
    }

    [Fact]
    public async Task VersionLifecycle_Publish_EditorForbidden_PublisherAllowed()
    {
        using var db = CreateDbContext();
        var editorToken = await GetEditorTokenAsync();
        var publisherToken = await GetPublisherTokenAsync();

        // Create a minimal valid draft version
        var update = new GameUpdate { Id = Guid.NewGuid(), Version = $"pub-test-{Guid.NewGuid():N}"[..18] };
        db.GameUpdates.Add(update);
        var tree = await db.ResearchTrees.FirstAsync(t => t.Vehicles.Any());
        var validDraft = new ResearchTreeVersion
        {
            Id = Guid.NewGuid(),
            ResearchTreeId = tree.Id,
            GameUpdateId = update.Id,
            Status = ResearchTreeVersionStatus.Draft
        };
        // Rank 1 with 0 required unlocked vehicles and 1 vehicle entry
        var rank1 = new TreeRank { Id = Guid.NewGuid(), ResearchTreeVersionId = validDraft.Id, RankNumber = 1, RequiredVehiclesUnlocked = 0 };
        validDraft.TreeRanks.Add(rank1);
        var vehicle = await db.Vehicles.FirstAsync(v => v.ResearchTreeId == tree.Id);
        validDraft.Entries.Add(new VehicleTreeEntry
        {
            Id = Guid.NewGuid(),
            ResearchTreeVersionId = validDraft.Id,
            ResearchTreeId = validDraft.ResearchTreeId,
            VehicleId = vehicle.Id,
            TreeRankId = rank1.Id,
            RpCost = 1000,
            SlCost = 500,
            TreeColumn = 1,
            TreeRow = 1,
            AcquisitionType = VehicleAcquisitionType.Researchable
        });
        db.ResearchTreeVersions.Add(validDraft);
        await db.SaveChangesAsync();

        // Editor cannot publish (requires Publisher)
        using var editReq = CreateAuthorizedRequest(HttpMethod.Post, $"/api/admin/research-tree-versions/{validDraft.Id}/publish", editorToken);
        var editResp = await _client.SendAsync(editReq);
        Assert.Equal(HttpStatusCode.Forbidden, editResp.StatusCode);

        // Publisher can publish valid draft
        using var pubReq = CreateAuthorizedRequest(HttpMethod.Post, $"/api/admin/research-tree-versions/{validDraft.Id}/publish", publisherToken);
        var pubResp = await _client.SendAsync(pubReq);
        Assert.Equal(HttpStatusCode.NoContent, pubResp.StatusCode);

        // Attempting to publish already published version returns 409 Conflict
        using var rePubReq = CreateAuthorizedRequest(HttpMethod.Post, $"/api/admin/research-tree-versions/{validDraft.Id}/publish", publisherToken);
        var rePubResp = await _client.SendAsync(rePubReq);
        Assert.Equal(HttpStatusCode.Conflict, rePubResp.StatusCode);
    }

    [Fact]
    public async Task VersionLifecycle_Publish_ValidationFailure_Returns400()
    {
        using var db = CreateDbContext();
        var publisherToken = await GetPublisherTokenAsync();

        // Create an invalid draft (Rank 1 requires 5 vehicles, but 0 exist)
        var update = new GameUpdate { Id = Guid.NewGuid(), Version = $"inv-test-{Guid.NewGuid():N}"[..18] };
        db.GameUpdates.Add(update);
        var tree = await db.ResearchTrees.FirstAsync();
        var invalidDraft = new ResearchTreeVersion
        {
            Id = Guid.NewGuid(),
            ResearchTreeId = tree.Id,
            GameUpdateId = update.Id,
            Status = ResearchTreeVersionStatus.Draft
        };
        invalidDraft.TreeRanks.Add(new TreeRank
        {
            Id = Guid.NewGuid(),
            ResearchTreeVersionId = invalidDraft.Id,
            RankNumber = 1,
            RequiredVehiclesUnlocked = 5
        });
        db.ResearchTreeVersions.Add(invalidDraft);
        await db.SaveChangesAsync();

        using var pubReq = CreateAuthorizedRequest(HttpMethod.Post, $"/api/admin/research-tree-versions/{invalidDraft.Id}/publish", publisherToken);
        var pubResp = await _client.SendAsync(pubReq);
        Assert.Equal(HttpStatusCode.BadRequest, pubResp.StatusCode);
    }

    [Fact]
    public async Task VersionLifecycle_Delete_EditorAndPublisherForbidden_AdminAllowed()
    {
        using var db = CreateDbContext();
        var editorToken = await GetEditorTokenAsync();
        var publisherToken = await GetPublisherTokenAsync();
        var adminToken = await GetAdminTokenAsync();

        // Create a standalone draft to delete
        var update = new GameUpdate { Id = Guid.NewGuid(), Version = $"del-test-{Guid.NewGuid():N}"[..18] };
        db.GameUpdates.Add(update);
        var tree = await db.ResearchTrees.FirstAsync();
        var draft = new ResearchTreeVersion
        {
            Id = Guid.NewGuid(),
            ResearchTreeId = tree.Id,
            GameUpdateId = update.Id,
            Status = ResearchTreeVersionStatus.Draft
        };
        db.ResearchTreeVersions.Add(draft);
        await db.SaveChangesAsync();

        // Editor cannot delete version
        using var editReq = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/research-tree-versions/{draft.Id}", editorToken);
        var editResp = await _client.SendAsync(editReq);
        Assert.Equal(HttpStatusCode.Forbidden, editResp.StatusCode);

        // Publisher cannot delete version
        using var pubReq = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/research-tree-versions/{draft.Id}", publisherToken);
        var pubResp = await _client.SendAsync(pubReq);
        Assert.Equal(HttpStatusCode.Forbidden, pubResp.StatusCode);

        // Admin can delete draft version
        using var adminReq = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/research-tree-versions/{draft.Id}", adminToken);
        var adminResp = await _client.SendAsync(adminReq);
        Assert.Equal(HttpStatusCode.NoContent, adminResp.StatusCode);
    }

    [Fact]
    public async Task VersionLifecycle_DeletePublishedVersion_Returns409Conflict()
    {
        using var db = CreateDbContext();
        var adminToken = await GetAdminTokenAsync();

        // Create a published version
        var update = new GameUpdate { Id = Guid.NewGuid(), Version = $"pubdel-{Guid.NewGuid():N}"[..18] };
        db.GameUpdates.Add(update);
        var tree = await db.ResearchTrees.FirstAsync();
        var publishedVersion = new ResearchTreeVersion
        {
            Id = Guid.NewGuid(),
            ResearchTreeId = tree.Id,
            GameUpdateId = update.Id,
            Status = ResearchTreeVersionStatus.Published
        };
        db.ResearchTreeVersions.Add(publishedVersion);
        await db.SaveChangesAsync();

        // Attempting to delete published version returns 409 Conflict
        using var adminReq = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/research-tree-versions/{publishedVersion.Id}", adminToken);
        var adminResp = await _client.SendAsync(adminReq);
        Assert.Equal(HttpStatusCode.Conflict, adminResp.StatusCode);
    }

    #endregion

    #region Catalog Metadata Tests

    [Fact]
    public async Task Catalog_Nations_CrudWorkflowsAndPolicyGuards()
    {
        var editorToken = await GetEditorTokenAsync();
        var publisherToken = await GetPublisherTokenAsync();
        var adminToken = await GetAdminTokenAsync();

        // 1. Unauthenticated -> 401
        var unauthResp = await _client.GetAsync("/api/admin/nations");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthResp.StatusCode);

        // 2. Publisher cannot create nation -> 403
        using var pubCreate = CreateAuthorizedRequest(HttpMethod.Post, "/api/admin/nations", publisherToken, new CreateNationRequest("Sweden"));
        var pubCreateResp = await _client.SendAsync(pubCreate);
        Assert.Equal(HttpStatusCode.Forbidden, pubCreateResp.StatusCode);

        // 3. Editor creates nation -> 201
        var nationName = $"Nation_{Guid.NewGuid():N}"[..15];
        using var editCreate = CreateAuthorizedRequest(HttpMethod.Post, "/api/admin/nations", editorToken, new CreateNationRequest(nationName));
        var editCreateResp = await _client.SendAsync(editCreate);
        Assert.Equal(HttpStatusCode.Created, editCreateResp.StatusCode);
        var created = await editCreateResp.Content.ReadFromJsonAsync<NationDto>();
        Assert.NotNull(created);
        Assert.Equal(nationName, created.Name);

        // Duplicate name -> 409 Conflict
        using var dupCreate = CreateAuthorizedRequest(HttpMethod.Post, "/api/admin/nations", editorToken, new CreateNationRequest(nationName));
        var dupResp = await _client.SendAsync(dupCreate);
        Assert.Equal(HttpStatusCode.Conflict, dupResp.StatusCode);

        // 4. Editor updates nation -> 204
        var updatedName = nationName + "_upd";
        using var editUpdate = CreateAuthorizedRequest(HttpMethod.Put, $"/api/admin/nations/{created.Id}", editorToken, new UpdateNationRequest(updatedName));
        var editUpdateResp = await _client.SendAsync(editUpdate);
        Assert.Equal(HttpStatusCode.NoContent, editUpdateResp.StatusCode);

        // 5. Editor cannot delete nation -> 403
        using var editDelete = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/nations/{created.Id}", editorToken);
        var editDeleteResp = await _client.SendAsync(editDelete);
        Assert.Equal(HttpStatusCode.Forbidden, editDeleteResp.StatusCode);

        // 6. Admin deletes nation -> 204
        using var adminDelete = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/nations/{created.Id}", adminToken);
        var adminDeleteResp = await _client.SendAsync(adminDelete);
        Assert.Equal(HttpStatusCode.NoContent, adminDeleteResp.StatusCode);
    }

    [Fact]
    public async Task Catalog_VehicleTypes_CrudWorkflowsAndPolicyGuards()
    {
        var editorToken = await GetEditorTokenAsync();
        var adminToken = await GetAdminTokenAsync();

        var typeName = $"Type_{Guid.NewGuid():N}"[..15];

        // Editor create
        using var createReq = CreateAuthorizedRequest(HttpMethod.Post, "/api/admin/vehicle-types", editorToken, new CreateVehicleTypeRequest(typeName));
        var createResp = await _client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<VehicleTypeDto>();
        Assert.NotNull(created);

        // Editor update
        using var updateReq = CreateAuthorizedRequest(HttpMethod.Put, $"/api/admin/vehicle-types/{created.Id}", editorToken, new UpdateVehicleTypeRequest(typeName + "_v2"));
        var updateResp = await _client.SendAsync(updateReq);
        Assert.Equal(HttpStatusCode.NoContent, updateResp.StatusCode);

        // Admin delete
        using var deleteReq = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/vehicle-types/{created.Id}", adminToken);
        var deleteResp = await _client.SendAsync(deleteReq);
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
    }

    [Fact]
    public async Task Catalog_Vehicles_CrudWorkflowsAndPolicyGuards()
    {
        var editorToken = await GetEditorTokenAsync();
        var adminToken = await GetAdminTokenAsync();

        using var catalogDb = CreateDbContext();
        var vehicleName = $"Tank_{Guid.NewGuid():N}"[..15];

        // Editor create
        using var createReq = CreateAuthorizedRequest(HttpMethod.Post, "/api/admin/vehicles", editorToken, new CreateVehicleRequest((await catalogDb.ResearchTrees.FirstAsync()).Id, vehicleName, "https://example.com/tank.png"));
        var createResp = await _client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<VehicleDto>();
        Assert.NotNull(created);
        Assert.Equal(vehicleName, created.Name);

        // Editor update
        using var updateReq = CreateAuthorizedRequest(HttpMethod.Put, $"/api/admin/vehicles/{created.Id}", editorToken, new UpdateVehicleRequest(vehicleName + "_A", "https://example.com/tank_a.png"));
        var updateResp = await _client.SendAsync(updateReq);
        Assert.Equal(HttpStatusCode.NoContent, updateResp.StatusCode);

        // Admin delete unused vehicle
        using var deleteReq = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/vehicles/{created.Id}", adminToken);
        var deleteResp = await _client.SendAsync(deleteReq);
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        // Attempting to delete in-use vehicle returns 409 Conflict
        using var db = CreateDbContext();
        var inUseVehicle = await db.Vehicles.FirstAsync(v => v.TreeEntries.Any());
        using var deleteInUseReq = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/vehicles/{inUseVehicle.Id}", adminToken);
        var deleteInUseResp = await _client.SendAsync(deleteInUseReq);
        Assert.Equal(HttpStatusCode.Conflict, deleteInUseResp.StatusCode);
    }

    [Fact]
    public async Task Catalog_GameUpdates_CrudWorkflowsAndPolicyGuards()
    {
        var editorToken = await GetEditorTokenAsync();
        var adminToken = await GetAdminTokenAsync();

        var versionStr = $"2.{Random.Shared.Next(100, 999)}.{Random.Shared.Next(1, 99)}";

        // Editor create
        using var createReq = CreateAuthorizedRequest(HttpMethod.Post, "/api/admin/game-updates", editorToken, new CreateGameUpdateRequest(versionStr, "Test Update", new DateOnly(2026, 10, 1)));
        var createResp = await _client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<GameUpdateDto>();
        Assert.NotNull(created);
        Assert.Equal(versionStr, created.Version);

        // Editor update
        using var updateReq = CreateAuthorizedRequest(HttpMethod.Put, $"/api/admin/game-updates/{created.Id}", editorToken, new UpdateGameUpdateRequest(versionStr, "Updated Title", new DateOnly(2026, 10, 2)));
        var updateResp = await _client.SendAsync(updateReq);
        Assert.Equal(HttpStatusCode.NoContent, updateResp.StatusCode);

        // Admin delete unused update
        using var deleteReq = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/game-updates/{created.Id}", adminToken);
        var deleteResp = await _client.SendAsync(deleteReq);
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        // Attempting to delete in-use game update returns 409 Conflict
        using var db = CreateDbContext();
        var inUseUpdate = await db.GameUpdates.FirstAsync(gu => gu.ResearchTreeVersions.Any());
        using var deleteInUseReq = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/game-updates/{inUseUpdate.Id}", adminToken);
        var deleteInUseResp = await _client.SendAsync(deleteInUseReq);
        Assert.Equal(HttpStatusCode.Conflict, deleteInUseResp.StatusCode);
    }

    [Fact]
    public async Task GameUpdateWorkspaceReads_PublisherCanReadMetadataButCannotMutateCatalog()
    {
        var publisherToken = await GetPublisherTokenAsync();
        using var db = CreateDbContext();
        var update = await db.GameUpdates.AsNoTracking().OrderBy(gameUpdate => gameUpdate.Id).FirstAsync();

        using var readRequest = CreateAuthorizedRequest(HttpMethod.Get, $"/api/admin/game-updates/{update.Id}", publisherToken);
        var readResponse = await _client.SendAsync(readRequest);
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        var result = await readResponse.Content.ReadFromJsonAsync<GameUpdateDto>();
        Assert.Equal(update.Id, result?.Id);

        using var updateRequest = CreateAuthorizedRequest(HttpMethod.Put, $"/api/admin/game-updates/{update.Id}", publisherToken,
            new UpdateGameUpdateRequest(update.Version, update.Name, update.ReleaseDate));
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(updateRequest)).StatusCode);

        var nations = await _client.GetFromJsonAsync<List<NationReleaseProjection>>("/api/nations");
        Assert.Contains(nations!.SelectMany(nation => nation.ResearchTrees).SelectMany(tree => tree.Versions),
            version => version.GameUpdateId != Guid.Empty);
    }

    private sealed record NationReleaseProjection(List<TreeReleaseProjection> ResearchTrees);
    private sealed record TreeReleaseProjection(List<VersionReleaseProjection> Versions);
    private sealed record VersionReleaseProjection(Guid GameUpdateId);

    #endregion

    #region Draft Tree Editing Tests

    [Fact]
    public async Task DraftEditing_AllEndpoints_RejectPublishedVersionWith409Conflict()
    {
        using var db = CreateDbContext();
        var editorToken = await GetEditorTokenAsync();

        // Create draft version with rank and entry
        var update = new GameUpdate { Id = Guid.NewGuid(), Version = $"immut-{Guid.NewGuid():N}"[..18] };
        db.GameUpdates.Add(update);
        var tree = await db.ResearchTrees.FirstAsync(t => t.Vehicles.Any());
        var vehicle = await db.Vehicles.FirstAsync(v => v.ResearchTreeId == tree.Id);
        var publishedVersion = new ResearchTreeVersion
        {
            Id = Guid.NewGuid(),
            ResearchTreeId = tree.Id,
            GameUpdateId = update.Id,
            Status = ResearchTreeVersionStatus.Draft
        };
        var rank = new TreeRank { Id = Guid.NewGuid(), ResearchTreeVersionId = publishedVersion.Id, RankNumber = 1, RequiredVehiclesUnlocked = 1 };
        var entry = new VehicleTreeEntry
        {
            Id = Guid.NewGuid(),
            ResearchTreeVersionId = publishedVersion.Id,
            ResearchTreeId = publishedVersion.ResearchTreeId,
            VehicleId = vehicle.Id,
            TreeRankId = rank.Id,
            RpCost = 1000,
            SlCost = 500,
            TreeColumn = 1,
            TreeRow = 1,
            AcquisitionType = VehicleAcquisitionType.Researchable
        };
        publishedVersion.TreeRanks.Add(rank);
        publishedVersion.Entries.Add(entry);
        db.ResearchTreeVersions.Add(publishedVersion);
        await db.SaveChangesAsync();

        // Transition to published
        publishedVersion.Status = ResearchTreeVersionStatus.Published;
        await db.SaveChangesAsync();

        var pid = publishedVersion.Id;

        // 1. Add rank
        using var addRankReq = CreateAuthorizedRequest(HttpMethod.Post, $"/api/admin/drafts/{pid}/ranks", editorToken, new AddTreeRankRequest(2, 2));
        var addRankResp = await _client.SendAsync(addRankReq);
        Assert.Equal(HttpStatusCode.Conflict, addRankResp.StatusCode);

        // 2. Update rank
        using var updRankReq = CreateAuthorizedRequest(HttpMethod.Put, $"/api/admin/drafts/{pid}/ranks/{rank.Id}", editorToken, new UpdateTreeRankQuotaRequest(5));
        var updRankResp = await _client.SendAsync(updRankReq);
        Assert.Equal(HttpStatusCode.Conflict, updRankResp.StatusCode);

        // 3. Add entry
        using var addEntryReq = CreateAuthorizedRequest(HttpMethod.Post, $"/api/admin/drafts/{pid}/entries", editorToken, new AddVehicleTreeEntryRequest(Guid.NewGuid(), rank.Id, 2000, 1000, 2, 2));
        var addEntryResp = await _client.SendAsync(addEntryReq);
        Assert.Equal(HttpStatusCode.Conflict, addEntryResp.StatusCode);

        // 4. Update entry
        using var updEntryReq = CreateAuthorizedRequest(HttpMethod.Put, $"/api/admin/drafts/{pid}/entries/{entry.Id}", editorToken, new UpdateVehicleTreeEntryRequest(3000, 1500, 1, 1, rank.Id));
        var updEntryResp = await _client.SendAsync(updEntryReq);
        Assert.Equal(HttpStatusCode.Conflict, updEntryResp.StatusCode);

        // 5. Update entry costs
        using var updCostReq = CreateAuthorizedRequest(HttpMethod.Put, $"/api/admin/drafts/{pid}/entries/{entry.Id}/costs", editorToken, new UpdateEntryCostsRequest(4000, 2000));
        var updCostResp = await _client.SendAsync(updCostReq);
        Assert.Equal(HttpStatusCode.Conflict, updCostResp.StatusCode);

        // 6. Update entry coordinates
        using var updCoordReq = CreateAuthorizedRequest(HttpMethod.Put, $"/api/admin/drafts/{pid}/entries/{entry.Id}/coordinates", editorToken, new UpdateEntryCoordinatesRequest(3, 3));
        var updCoordResp = await _client.SendAsync(updCoordReq);
        Assert.Equal(HttpStatusCode.Conflict, updCoordResp.StatusCode);

        // 7. Update entry folder parent
        using var updParentReq = CreateAuthorizedRequest(HttpMethod.Put, $"/api/admin/drafts/{pid}/entries/{entry.Id}/folder-parent", editorToken, new UpdateEntryFolderParentRequest(null));
        var updParentResp = await _client.SendAsync(updParentReq);
        Assert.Equal(HttpStatusCode.Conflict, updParentResp.StatusCode);

        // 8. Delete entry
        using var delEntryReq = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/drafts/{pid}/entries/{entry.Id}", editorToken);
        var delEntryResp = await _client.SendAsync(delEntryReq);
        Assert.Equal(HttpStatusCode.Conflict, delEntryResp.StatusCode);

        // 9. Add prerequisite
        using var addPrereqReq = CreateAuthorizedRequest(HttpMethod.Post, $"/api/admin/drafts/{pid}/prerequisites", editorToken, new AddPrerequisiteRequest(entry.Id, Guid.NewGuid()));
        var addPrereqResp = await _client.SendAsync(addPrereqReq);
        Assert.Equal(HttpStatusCode.Conflict, addPrereqResp.StatusCode);

        // 10. Remove prerequisite
        using var delPrereqReq = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/drafts/{pid}/prerequisites/{entry.Id}/{Guid.NewGuid()}", editorToken);
        var delPrereqResp = await _client.SendAsync(delPrereqReq);
        Assert.Equal(HttpStatusCode.Conflict, delPrereqResp.StatusCode);
    }

    [Fact]
    public async Task DraftEditing_TreeRanks_ManageWorkflow()
    {
        using var db = CreateDbContext();
        var editorToken = await GetEditorTokenAsync();

        // Use a draft version
        var draft = await db.ResearchTreeVersions.FirstAsync(v => v.Status == ResearchTreeVersionStatus.Draft);

        // 1. Add rank
        var maxExistingRank = await db.TreeRanks.Where(r => r.ResearchTreeVersionId == draft.Id).MaxAsync(r => (int?)r.RankNumber) ?? 0;
        var newRankNumber = maxExistingRank + 1;

        using var addReq = CreateAuthorizedRequest(HttpMethod.Post, $"/api/admin/drafts/{draft.Id}/ranks", editorToken, new AddTreeRankRequest(newRankNumber, 4));
        var addResp = await _client.SendAsync(addReq);
        Assert.Equal(HttpStatusCode.Created, addResp.StatusCode);
        var created = await addResp.Content.ReadFromJsonAsync<TreeRankDto>();
        Assert.NotNull(created);
        Assert.Equal(newRankNumber, created.RankNumber);
        Assert.Equal(4, created.RequiredVehiclesUnlocked);

        // Duplicate rank -> 409 Conflict
        using var dupReq = CreateAuthorizedRequest(HttpMethod.Post, $"/api/admin/drafts/{draft.Id}/ranks", editorToken, new AddTreeRankRequest(newRankNumber, 5));
        var dupResp = await _client.SendAsync(dupReq);
        Assert.Equal(HttpStatusCode.Conflict, dupResp.StatusCode);

        // Negative rank number -> 400 Bad Request
        using var negReq = CreateAuthorizedRequest(HttpMethod.Post, $"/api/admin/drafts/{draft.Id}/ranks", editorToken, new AddTreeRankRequest(-1, 5));
        var negResp = await _client.SendAsync(negReq);
        Assert.Equal(HttpStatusCode.BadRequest, negResp.StatusCode);

        // 2. Update quota
        using var updReq = CreateAuthorizedRequest(HttpMethod.Put, $"/api/admin/drafts/{draft.Id}/ranks/{created.Id}", editorToken, new UpdateTreeRankQuotaRequest(6));
        var updResp = await _client.SendAsync(updReq);
        Assert.Equal(HttpStatusCode.NoContent, updResp.StatusCode);

        // Verify updated in DB
        using var verifyDb = CreateDbContext();
        var refreshedRank = await verifyDb.TreeRanks.FindAsync(created.Id);
        Assert.NotNull(refreshedRank);
        Assert.Equal(6, refreshedRank.RequiredVehiclesUnlocked);
    }

    [Fact]
    public async Task DraftEditing_VehicleTreeEntries_ManageWorkflow()
    {
        using var db = CreateDbContext();
        var editorToken = await GetEditorTokenAsync();

        var draft = await db.ResearchTreeVersions.Include(v => v.TreeRanks).FirstAsync(v => v.Status == ResearchTreeVersionStatus.Draft);
        var rank = draft.TreeRanks.First();

        // Create new vehicle
        var vehicle = new Vehicle { Id = Guid.NewGuid(), ResearchTreeId = draft.ResearchTreeId, Name = $"DraftTank_{Guid.NewGuid():N}"[..18] };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();

        // 1. Add entry
        using var addReq = CreateAuthorizedRequest(HttpMethod.Post, $"/api/admin/drafts/{draft.Id}/entries", editorToken,
            new AddVehicleTreeEntryRequest(vehicle.Id, rank.Id, 5000, 2500, 3, 1, VehicleAcquisitionType.Researchable));
        var addResp = await _client.SendAsync(addReq);
        Assert.Equal(HttpStatusCode.Created, addResp.StatusCode);
        var created = await addResp.Content.ReadFromJsonAsync<VehicleTreeEntryDto>();
        Assert.NotNull(created);
        Assert.Equal(5000, created.RpCost);

        // Duplicate vehicle in version -> 409 Conflict
        using var dupReq = CreateAuthorizedRequest(HttpMethod.Post, $"/api/admin/drafts/{draft.Id}/entries", editorToken,
            new AddVehicleTreeEntryRequest(vehicle.Id, rank.Id, 6000, 3000, 3, 2));
        var dupResp = await _client.SendAsync(dupReq);
        Assert.Equal(HttpStatusCode.Conflict, dupResp.StatusCode);

        // 2. Update costs
        using var costReq = CreateAuthorizedRequest(HttpMethod.Put, $"/api/admin/drafts/{draft.Id}/entries/{created.Id}/costs", editorToken, new UpdateEntryCostsRequest(7000, 3500));
        var costResp = await _client.SendAsync(costReq);
        Assert.Equal(HttpStatusCode.NoContent, costResp.StatusCode);

        // 3. Update coordinates
        using var coordReq = CreateAuthorizedRequest(HttpMethod.Put, $"/api/admin/drafts/{draft.Id}/entries/{created.Id}/coordinates", editorToken, new UpdateEntryCoordinatesRequest(4, 2));
        var coordResp = await _client.SendAsync(coordReq);
        Assert.Equal(HttpStatusCode.NoContent, coordResp.StatusCode);

        // 4. Update folder parent
        using var parentReq = CreateAuthorizedRequest(HttpMethod.Put, $"/api/admin/drafts/{draft.Id}/entries/{created.Id}/folder-parent", editorToken, new UpdateEntryFolderParentRequest(null));
        var parentResp = await _client.SendAsync(parentReq);
        Assert.Equal(HttpStatusCode.NoContent, parentResp.StatusCode);

        // 5. Delete entry
        using var delReq = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/drafts/{draft.Id}/entries/{created.Id}", editorToken);
        var delResp = await _client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);
    }

    [Fact]
    public async Task DraftEditing_VehiclePrerequisites_ManageWorkflow()
    {
        using var db = CreateDbContext();
        var editorToken = await GetEditorTokenAsync();

        var update = new GameUpdate { Id = Guid.NewGuid(), Version = $"prereq-{Guid.NewGuid():N}"[..18] };
        db.GameUpdates.Add(update);
        var tree = await db.ResearchTrees.FirstAsync();
        var draft = new ResearchTreeVersion
        {
            Id = Guid.NewGuid(),
            ResearchTreeId = tree.Id,
            GameUpdateId = update.Id,
            Status = ResearchTreeVersionStatus.Draft
        };
        var rank = new TreeRank { Id = Guid.NewGuid(), ResearchTreeVersionId = draft.Id, RankNumber = 1, RequiredVehiclesUnlocked = 0 };
        draft.TreeRanks.Add(rank);

        var v1 = new Vehicle { Id = Guid.NewGuid(), ResearchTreeId = draft.ResearchTreeId, Name = $"PV1_{Guid.NewGuid():N}"[..15] };
        var v2 = new Vehicle { Id = Guid.NewGuid(), ResearchTreeId = draft.ResearchTreeId, Name = $"PV2_{Guid.NewGuid():N}"[..15] };
        db.Vehicles.AddRange(v1, v2);

        var entry1 = new VehicleTreeEntry
        {
            Id = Guid.NewGuid(),
            ResearchTreeVersionId = draft.Id,
            ResearchTreeId = draft.ResearchTreeId,
            VehicleId = v1.Id,
            TreeRankId = rank.Id,
            RpCost = 1000,
            SlCost = 500,
            TreeColumn = 1,
            TreeRow = 1,
            AcquisitionType = VehicleAcquisitionType.Researchable
        };
        var entry2 = new VehicleTreeEntry
        {
            Id = Guid.NewGuid(),
            ResearchTreeVersionId = draft.Id,
            ResearchTreeId = draft.ResearchTreeId,
            VehicleId = v2.Id,
            TreeRankId = rank.Id,
            RpCost = 2000,
            SlCost = 1000,
            TreeColumn = 1,
            TreeRow = 2,
            AcquisitionType = VehicleAcquisitionType.Researchable
        };
        draft.Entries.Add(entry1);
        draft.Entries.Add(entry2);
        db.ResearchTreeVersions.Add(draft);
        await db.SaveChangesAsync();

        // 1. Add edge
        using var addReq = CreateAuthorizedRequest(HttpMethod.Post, $"/api/admin/drafts/{draft.Id}/prerequisites", editorToken,
            new AddPrerequisiteRequest(entry2.Id, entry1.Id));
        var addResp = await _client.SendAsync(addReq);
        Assert.Equal(HttpStatusCode.Created, addResp.StatusCode);
        var edge = await addResp.Content.ReadFromJsonAsync<VehiclePrerequisiteDto>();
        Assert.NotNull(edge);
        Assert.Equal(entry2.Id, edge.VehicleTreeEntryId);
        Assert.Equal(entry1.Id, edge.PrerequisiteVehicleTreeEntryId);

        // Duplicate edge -> 409 Conflict
        using var dupReq = CreateAuthorizedRequest(HttpMethod.Post, $"/api/admin/drafts/{draft.Id}/prerequisites", editorToken,
            new AddPrerequisiteRequest(entry2.Id, entry1.Id));
        var dupResp = await _client.SendAsync(dupReq);
        Assert.Equal(HttpStatusCode.Conflict, dupResp.StatusCode);

        // Self-reference edge -> 400 Bad Request
        using var selfReq = CreateAuthorizedRequest(HttpMethod.Post, $"/api/admin/drafts/{draft.Id}/prerequisites", editorToken,
            new AddPrerequisiteRequest(entry1.Id, entry1.Id));
        var selfResp = await _client.SendAsync(selfReq);
        Assert.Equal(HttpStatusCode.BadRequest, selfResp.StatusCode);

        // 2. Remove edge via route
        using var delReq = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/drafts/{draft.Id}/prerequisites/{entry2.Id}/{entry1.Id}", editorToken);
        var delResp = await _client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

        // Removing non-existent edge -> 404 Not Found
        using var delAgainReq = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/drafts/{draft.Id}/prerequisites/{entry2.Id}/{entry1.Id}", editorToken);
        var delAgainResp = await _client.SendAsync(delAgainReq);
        Assert.Equal(HttpStatusCode.NotFound, delAgainResp.StatusCode);
    }

    [Fact]
    public async Task DraftEditing_TreeRanks_DeleteWorkflow()
    {
        using var db = CreateDbContext();
        var editorToken = await GetEditorTokenAsync();

        var update = new GameUpdate { Id = Guid.NewGuid(), Version = $"delrank-{Guid.NewGuid():N}"[..18] };
        db.GameUpdates.Add(update);
        var tree = await db.ResearchTrees.FirstAsync();
        var draft = new ResearchTreeVersion
        {
            Id = Guid.NewGuid(),
            ResearchTreeId = tree.Id,
            GameUpdateId = update.Id,
            Status = ResearchTreeVersionStatus.Draft
        };
        var rank1 = new TreeRank { Id = Guid.NewGuid(), ResearchTreeVersionId = draft.Id, RankNumber = 1, RequiredVehiclesUnlocked = 0 };
        var rank2 = new TreeRank { Id = Guid.NewGuid(), ResearchTreeVersionId = draft.Id, RankNumber = 2, RequiredVehiclesUnlocked = 1 };
        draft.TreeRanks.Add(rank1);
        draft.TreeRanks.Add(rank2);

        var v1 = new Vehicle { Id = Guid.NewGuid(), ResearchTreeId = draft.ResearchTreeId, Name = $"RankTank_{Guid.NewGuid():N}"[..18] };
        db.Vehicles.Add(v1);

        var entry1 = new VehicleTreeEntry
        {
            Id = Guid.NewGuid(),
            ResearchTreeVersionId = draft.Id,
            ResearchTreeId = draft.ResearchTreeId,
            VehicleId = v1.Id,
            TreeRankId = rank1.Id,
            RpCost = 1000,
            SlCost = 500,
            TreeColumn = 1,
            TreeRow = 1,
            AcquisitionType = VehicleAcquisitionType.Researchable
        };
        draft.Entries.Add(entry1);
        db.ResearchTreeVersions.Add(draft);
        await db.SaveChangesAsync();

        // 1. Delete rank containing entries -> 409 Conflict
        using var delRank1Req = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/drafts/{draft.Id}/ranks/{rank1.Id}", editorToken);
        var delRank1Resp = await _client.SendAsync(delRank1Req);
        Assert.Equal(HttpStatusCode.Conflict, delRank1Resp.StatusCode);

        // 2. Delete empty rank -> 204 NoContent
        using var delRank2Req = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/drafts/{draft.Id}/ranks/{rank2.Id}", editorToken);
        var delRank2Resp = await _client.SendAsync(delRank2Req);
        Assert.Equal(HttpStatusCode.NoContent, delRank2Resp.StatusCode);

        // 3. Delete non-existent rank -> 404 NotFound
        using var delAgainReq = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/drafts/{draft.Id}/ranks/{rank2.Id}", editorToken);
        var delAgainResp = await _client.SendAsync(delAgainReq);
        Assert.Equal(HttpStatusCode.NotFound, delAgainResp.StatusCode);
    }

    [Fact]
    public async Task DraftEditing_VehicleTreeEntries_InvalidAcquisitionType_Returns400BadRequest()
    {
        using var db = CreateDbContext();
        var editorToken = await GetEditorTokenAsync();

        var draft = await db.ResearchTreeVersions.Include(v => v.TreeRanks).FirstAsync(v => v.Status == ResearchTreeVersionStatus.Draft);
        var rank = draft.TreeRanks.First();
        var vehicle = new Vehicle { Id = Guid.NewGuid(), ResearchTreeId = draft.ResearchTreeId, Name = $"EnumTank_{Guid.NewGuid():N}"[..18] };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();

        // Add with invalid AcquisitionType enum value -> 400 BadRequest
        using var addReq = CreateAuthorizedRequest(HttpMethod.Post, $"/api/admin/drafts/{draft.Id}/entries", editorToken,
            new AddVehicleTreeEntryRequest(vehicle.Id, rank.Id, 1000, 500, 1, 1, (VehicleAcquisitionType)999));
        var addResp = await _client.SendAsync(addReq);
        Assert.Equal(HttpStatusCode.BadRequest, addResp.StatusCode);
    }

    [Fact]
    public async Task ResearchTrees_CrudWorkflowsAndPolicyGuards()
    {
        var editorToken = await GetEditorTokenAsync();
        var adminToken = await GetAdminTokenAsync();

        using var db = CreateDbContext();
        var nation = new Nation { Id = Guid.NewGuid(), Name = $"RT_Nation_{Guid.NewGuid():N}"[..18] };
        var vehicleType = new VehicleType { Id = Guid.NewGuid(), Name = $"RT_Type_{Guid.NewGuid():N}"[..18] };
        db.Nations.Add(nation);
        db.VehicleTypes.Add(vehicleType);
        await db.SaveChangesAsync();

        // 1. Editor create ResearchTree -> 201 Created
        using var createReq = CreateAuthorizedRequest(HttpMethod.Post, "/api/admin/research-trees", editorToken,
            new CreateResearchTreeRequest(nation.Id, vehicleType.Id));
        var createResp = await _client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<ResearchTreeDto>();
        Assert.NotNull(created);
        Assert.Equal(nation.Id, created.NationId);
        Assert.Equal(vehicleType.Id, created.VehicleTypeId);

        // 2. Duplicate ResearchTree -> 409 Conflict
        using var dupReq = CreateAuthorizedRequest(HttpMethod.Post, "/api/admin/research-trees", editorToken,
            new CreateResearchTreeRequest(nation.Id, vehicleType.Id));
        var dupResp = await _client.SendAsync(dupReq);
        Assert.Equal(HttpStatusCode.Conflict, dupResp.StatusCode);

        // 3. Get all -> 200 OK
        using var getAllReq = CreateAuthorizedRequest(HttpMethod.Get, "/api/admin/research-trees", editorToken);
        var getAllResp = await _client.SendAsync(getAllReq);
        Assert.Equal(HttpStatusCode.OK, getAllResp.StatusCode);
        var allTrees = await getAllResp.Content.ReadFromJsonAsync<List<ResearchTreeDto>>();
        Assert.NotNull(allTrees);
        Assert.Contains(allTrees, t => t.Id == created.Id);

        // 4. Get by ID -> 200 OK
        using var getByIdReq = CreateAuthorizedRequest(HttpMethod.Get, $"/api/admin/research-trees/{created.Id}", editorToken);
        var getByIdResp = await _client.SendAsync(getByIdReq);
        Assert.Equal(HttpStatusCode.OK, getByIdResp.StatusCode);

        // 5. Editor cannot delete ResearchTree -> 403 Forbidden
        using var editDelReq = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/research-trees/{created.Id}", editorToken);
        var editDelResp = await _client.SendAsync(editDelReq);
        Assert.Equal(HttpStatusCode.Forbidden, editDelResp.StatusCode);

        // 6. Admin delete in-use ResearchTree -> 409 Conflict
        var inUseTree = await db.ResearchTrees.FirstAsync(rt => rt.Versions.Any());
        using var delInUseReq = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/research-trees/{inUseTree.Id}", adminToken);
        var delInUseResp = await _client.SendAsync(delInUseReq);
        Assert.Equal(HttpStatusCode.Conflict, delInUseResp.StatusCode);

        // 7. Admin delete empty ResearchTree -> 204 NoContent
        using var adminDelReq = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/research-trees/{created.Id}", adminToken);
        var adminDelResp = await _client.SendAsync(adminDelReq);
        Assert.Equal(HttpStatusCode.NoContent, adminDelResp.StatusCode);

        // 8. Delete non-existent -> 404 NotFound
        using var delMissingReq = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/admin/research-trees/{created.Id}", adminToken);
        var delMissingResp = await _client.SendAsync(delMissingReq);
        Assert.Equal(HttpStatusCode.NotFound, delMissingResp.StatusCode);
    }

    [Fact]
    public async Task VersionLifecycle_CreateInitialDraftVersion_FromScratch()
    {
        var editorToken = await GetEditorTokenAsync();

        using var db = CreateDbContext();
        var nation = new Nation { Id = Guid.NewGuid(), Name = $"DraftNation_{Guid.NewGuid():N}"[..18] };
        var vehicleType = new VehicleType { Id = Guid.NewGuid(), Name = $"DraftType_{Guid.NewGuid():N}"[..18] };
        var tree = new ResearchTree { Id = Guid.NewGuid(), NationId = nation.Id, VehicleTypeId = vehicleType.Id };
        var update = new GameUpdate { Id = Guid.NewGuid(), Version = $"ScratchUpd_{Guid.NewGuid():N}"[..18] };
        db.Nations.Add(nation);
        db.VehicleTypes.Add(vehicleType);
        db.ResearchTrees.Add(tree);
        db.GameUpdates.Add(update);
        await db.SaveChangesAsync();

        // 1. Create blank draft version -> 201 Created
        using var createReq = CreateAuthorizedRequest(HttpMethod.Post, "/api/admin/research-tree-versions", editorToken,
            new CreateDraftVersionRequest(tree.Id, update.Id));
        var createResp = await _client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<ClonedVersionResponse>();
        Assert.NotNull(created);
        Assert.Equal("Draft", created.Status);
        Assert.Equal(tree.Id, created.ResearchTreeId);
        Assert.Equal(update.Id, created.GameUpdateId);

        // 2. Duplicate draft for same tree and update -> 409 Conflict
        using var dupReq = CreateAuthorizedRequest(HttpMethod.Post, "/api/admin/research-tree-versions", editorToken,
            new CreateDraftVersionRequest(tree.Id, update.Id));
        var dupResp = await _client.SendAsync(dupReq);
        Assert.Equal(HttpStatusCode.Conflict, dupResp.StatusCode);

        // 3. Missing research tree -> 400 BadRequest
        using var missingTreeReq = CreateAuthorizedRequest(HttpMethod.Post, "/api/admin/research-tree-versions", editorToken,
            new CreateDraftVersionRequest(Guid.NewGuid(), update.Id));
        var missingTreeResp = await _client.SendAsync(missingTreeReq);
        Assert.Equal(HttpStatusCode.BadRequest, missingTreeResp.StatusCode);
    }

    #endregion
}
