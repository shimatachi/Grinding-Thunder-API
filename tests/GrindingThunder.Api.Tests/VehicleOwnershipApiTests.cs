using System.Net;
using System.Net.Http.Json;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using GrindingThunder.Api.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class VehicleOwnershipApiTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();
    public Task InitializeAsync() => factory.InitializeDatabaseAsync();
    public Task DisposeAsync() { _client.Dispose(); return Task.CompletedTask; }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new("Bearer", await factory.GetTokenAsync("admin", "AdminPass123!"));
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task VehicleCreate_RequiresExistingTree_AndReturnsOwnershipContext()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, "/api/admin/vehicles", new { name = "Missing owner" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, "/api/admin/vehicles", new CreateVehicleRequest(Guid.NewGuid(), "Unknown tree"))).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var trees = await db.ResearchTrees.Include(t => t.Nation).Include(t => t.VehicleType).Take(2).ToListAsync();
        var name = $"Shared {Guid.NewGuid():N}";
        var firstResponse = await SendAsync(HttpMethod.Post, "/api/admin/vehicles", new CreateVehicleRequest(trees[0].Id, name));
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var first = (await firstResponse.Content.ReadFromJsonAsync<VehicleDto>())!;
        Assert.Equal(trees[0].Id, first.ResearchTreeId);
        Assert.Equal(trees[0].NationId, first.NationId);
        Assert.Equal(trees[0].Nation.Name, first.NationName);
        Assert.Equal(trees[0].VehicleTypeId, first.VehicleTypeId);
        Assert.Equal(trees[0].VehicleType.Name, first.VehicleTypeName);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(HttpMethod.Post, "/api/admin/vehicles",
            new CreateVehicleRequest(trees[0].Id, $" {name.ToUpperInvariant()} "))).StatusCode);
        var secondResponse = await SendAsync(HttpMethod.Post, "/api/admin/vehicles", new CreateVehicleRequest(trees[1].Id, name));
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var second = (await secondResponse.Content.ReadFromJsonAsync<VehicleDto>())!;
        Assert.NotEqual(first.Id, second.Id);
        var extraResponse = await SendAsync(HttpMethod.Post, "/api/admin/vehicles", new CreateVehicleRequest(trees[0].Id, name + " extra"));
        var extra = (await extraResponse.Content.ReadFromJsonAsync<VehicleDto>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(HttpMethod.Put, $"/api/admin/vehicles/{extra.Id}", new UpdateVehicleRequest(name.ToUpperInvariant()))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Put, $"/api/admin/vehicles/{second.Id}", new UpdateVehicleRequest(name.ToUpperInvariant()))).StatusCode);
        var listed = (await (await SendAsync(HttpMethod.Get, "/api/admin/vehicles")).Content.ReadFromJsonAsync<List<VehicleDto>>())!;
        Assert.Contains(listed, v => v.Id == first.Id && v.ResearchTreeId == trees[0].Id);
        using var publicVehicles = System.Text.Json.JsonDocument.Parse(await _client.GetStringAsync("/api/vehicles"));
        var publicVehicle = publicVehicles.RootElement.EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == first.Id);
        Assert.Equal(first.ResearchTreeId, publicVehicle.GetProperty("researchTreeId").GetGuid());
        Assert.Equal(first.NationName, publicVehicle.GetProperty("nationName").GetString());
        Assert.Equal(first.VehicleTypeName, publicVehicle.GetProperty("vehicleTypeName").GetString());
    }

    [Fact]
    public async Task DraftEntry_ForeignTreeReturns400_LocalTreeReturns201()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var source = await db.ResearchTreeVersions.FirstAsync(v => v.Entries.Any());
        var update = new GameUpdate { Id = Guid.NewGuid(), Version = Guid.NewGuid().ToString() };
        db.GameUpdates.Add(update);
        await db.SaveChangesAsync();
        var clone = await new VersionLifecycleService(db).CloneVersionAsync(source.Id, update.Id);
        var rank = await db.TreeRanks.FirstAsync(r => r.ResearchTreeVersionId == clone.Id);
        var otherTree = await db.ResearchTrees.FirstAsync(t => t.Id != clone.ResearchTreeId);
        var foreign = new Vehicle { Id = Guid.NewGuid(), ResearchTreeId = otherTree.Id, Name = $"Foreign {Guid.NewGuid():N}" };
        var local = new Vehicle { Id = Guid.NewGuid(), ResearchTreeId = clone.ResearchTreeId, Name = foreign.Name };
        db.Vehicles.AddRange(foreign, local);
        await db.SaveChangesAsync();
        var rejection = await SendAsync(HttpMethod.Post, $"/api/admin/drafts/{clone.Id}/entries",
            new AddVehicleTreeEntryRequest(foreign.Id, rank.Id, 100, 100, 9, 9));
        Assert.Equal(HttpStatusCode.BadRequest, rejection.StatusCode);
        Assert.Contains("does not belong", await rejection.Content.ReadAsStringAsync());
        Assert.False(await db.VehicleTreeEntries.AnyAsync(e => e.ResearchTreeVersionId == clone.Id && e.VehicleId == foreign.Id));
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(HttpMethod.Post, $"/api/admin/drafts/{clone.Id}/entries",
            new AddVehicleTreeEntryRequest(local.Id, rank.Id, 100, 100, 9, 9))).StatusCode);
    }
}
