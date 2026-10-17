using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class PublicResearchTreeIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PublicResearchTreeIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabaseAsync().GetAwaiter().GetResult();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ExactPublishedVersionTree_ReturnsOnlyThatSnapshotsEntries()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var version = await db.ResearchTreeVersions
            .AsNoTracking()
            .Include(item => item.ResearchTree).ThenInclude(tree => tree.VehicleType)
            .Include(item => item.ResearchTree).ThenInclude(tree => tree.Nation)
            .Include(item => item.Entries)
            .FirstAsync(item => item.Status == ResearchTreeVersionStatus.Published && item.Entries.Count > 0);

        var response = await _client.GetAsync(
            $"/api/vehicles/tree?nationId={version.ResearchTree.NationId}" +
            $"&researchTreeVersionId={version.Id}" +
            $"&type={Uri.EscapeDataString(version.ResearchTree.VehicleType.Name)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var entries = payload.RootElement.EnumerateArray().ToList();
        Assert.Equal(version.Entries.Count, entries.Count);
        Assert.All(entries, entry =>
            Assert.Equal(version.Id, entry.GetProperty("researchTreeVersionId").GetGuid()));
    }

    [Fact]
    public async Task ExactDraftVersionTree_ReturnsNotFoundAndPublicCatalogOmitsDraft()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tree = await db.ResearchTrees.AsNoTracking()
            .Include(item => item.VehicleType)
            .Include(item => item.Nation)
            .OrderBy(item => item.Id)
            .FirstAsync();
        var update = new GameUpdate { Id = Guid.NewGuid(), Version = $"draft-{Guid.NewGuid():N}" };
        var draft = new ResearchTreeVersion
        {
            Id = Guid.NewGuid(),
            ResearchTreeId = tree.Id,
            GameUpdateId = update.Id,
            Status = ResearchTreeVersionStatus.Draft
        };
        db.GameUpdates.Add(update);
        db.ResearchTreeVersions.Add(draft);
        await db.SaveChangesAsync();

        var treeResponse = await _client.GetAsync(
            $"/api/vehicles/tree?nationId={tree.NationId}&researchTreeVersionId={draft.Id}" +
            $"&type={Uri.EscapeDataString(tree.VehicleType.Name)}");
        Assert.Equal(HttpStatusCode.NotFound, treeResponse.StatusCode);

        var catalog = await _client.GetFromJsonAsync<JsonElement[]>("/api/nations");
        Assert.NotNull(catalog);
        Assert.DoesNotContain(catalog.SelectMany(nation => nation.GetProperty("researchTrees").EnumerateArray())
            .SelectMany(researchTree => researchTree.GetProperty("versions").EnumerateArray()),
            version => version.GetProperty("id").GetGuid() == draft.Id);

        var unauthorizedDraftCatalog = await _client.GetAsync("/api/nations?includeDrafts=true");
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedDraftCatalog.StatusCode);
    }
}
