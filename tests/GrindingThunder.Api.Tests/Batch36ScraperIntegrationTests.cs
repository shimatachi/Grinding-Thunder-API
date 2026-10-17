using System.Net;
using System.Net.Http.Json;
using System.Text;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using GrindingThunder.Api.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class Batch36ScraperIntegrationTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public Batch36ScraperIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.InitializeDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private ApplicationDbContext CreateDbContext()
    {
        var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    [Fact]
    public async Task Vehicle_Entity_StoresNullableWikiUrl_AndPreservesExistingData()
    {
        using var db = CreateDbContext();
        var tree = await db.ResearchTrees.FirstAsync();

        // 1. Can store vehicle with null WikiUrl
        var vNull = new Vehicle
        {
            Id = Guid.NewGuid(),
            Name = $"NullWiki_{Guid.NewGuid():N}"[..15],
            ResearchTreeId = tree.Id,
            ImageUrl = "https://example.test/img.png",
            WikiUrl = null
        };
        db.Vehicles.Add(vNull);

        // 2. Can store vehicle with valid WikiUrl
        var wiki = "https://wiki.warthunder.com/Test_Vehicle";
        var vWithWiki = new Vehicle
        {
            Id = Guid.NewGuid(),
            Name = $"WithWiki_{Guid.NewGuid():N}"[..15],
            ResearchTreeId = tree.Id,
            ImageUrl = "https://example.test/img2.png",
            WikiUrl = wiki
        };
        db.Vehicles.Add(vWithWiki);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        var loadedNull = await db.Vehicles.FindAsync(vNull.Id);
        Assert.NotNull(loadedNull);
        Assert.Null(loadedNull.WikiUrl);

        var loadedWithWiki = await db.Vehicles.FindAsync(vWithWiki.Id);
        Assert.NotNull(loadedWithWiki);
        Assert.Equal(wiki, loadedWithWiki.WikiUrl);
    }

    [Fact]
    public async Task PublicVehicleEndpoints_ProjectWikiUrl()
    {
        using var db = CreateDbContext();
        var sampleVehicle = await db.Vehicles.FirstAsync(v => !string.IsNullOrEmpty(v.WikiUrl));

        // 1. GET /api/vehicles
        var vehiclesResp = await _client.GetAsync("/api/vehicles");
        Assert.Equal(HttpStatusCode.OK, vehiclesResp.StatusCode);
        var vehiclesJson = await vehiclesResp.Content.ReadAsStringAsync();
        using var vehiclesDoc = System.Text.Json.JsonDocument.Parse(vehiclesJson);
        var matched = vehiclesDoc.RootElement.EnumerateArray()
            .FirstOrDefault(v => v.GetProperty("id").GetGuid() == sampleVehicle.Id);
        Assert.Equal(sampleVehicle.WikiUrl, matched.GetProperty("wikiUrl").GetString());

        // 2. GET /api/vehicles/tree
        var version = await db.ResearchTreeVersions
            .Include(v => v.ResearchTree)
            .ThenInclude(rt => rt.VehicleType)
            .Include(v => v.Entries)
            .FirstAsync(v => v.Status == ResearchTreeVersionStatus.Published && v.Entries.Any(e => e.VehicleId == sampleVehicle.Id));

        var treeResp = await _client.GetAsync($"/api/vehicles/tree?nationId={version.ResearchTree.NationId}&researchTreeVersionId={version.Id}&type={version.ResearchTree.VehicleType.Name}");
        Assert.Equal(HttpStatusCode.OK, treeResp.StatusCode);
        var treeJson = await treeResp.Content.ReadAsStringAsync();
        using var treeDoc = System.Text.Json.JsonDocument.Parse(treeJson);
        var treeEntry = treeDoc.RootElement.EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("id").GetGuid() == sampleVehicle.Id);
        Assert.Equal(sampleVehicle.WikiUrl, treeEntry.GetProperty("wikiUrl").GetString());
    }

    [Fact]
    public async Task Import_AutoCreatedVehicle_PersistsImageUrlAndWikiUrl()
    {
        var testDb = new CalculatorTestDatabase();
        using (testDb)
        {
            var nation = testDb.AddNation("Israel");
            var aviation = testDb.AddVehicleType("Aviation");
            var tree = testDb.AddResearchTree(nation, aviation);
            var update = testDb.AddGameUpdate("2.43");
            var draft = testDb.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);
            await testDb.SaveChangesAsync();

            var importService = new TechTreeImportService(testDb.Context);

            var vehicleName = "Kfir C.7";
            var imageUrl = "https://static.encyclopedia.warthunder.com/images/kfir_c7.png";
            var wikiUrl = "https://wiki.warthunder.com/unit/kfir_c7";

            var csv = $"""
                VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow,AcquisitionType,FolderParentVehicleName,PrerequisiteVehicleNames,RequiredVehiclesUnlocked,ImageUrl,WikiUrl
                {vehicleName},7,390000,1010000,0,0,Researchable,,,,{imageUrl},{wikiUrl}
                """;

            using var commitStream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var commitResult = await importService.CommitImportAsync(draft.Id, commitStream, "test_israel.csv");

            Assert.True(commitResult.Success);
            Assert.Equal(1, commitResult.AutoCreatedVehiclesCount);

            testDb.Context.ChangeTracker.Clear();

            var createdVehicle = await testDb.Context.Vehicles.SingleAsync(v => v.Name == vehicleName && v.ResearchTreeId == tree.Id);
            Assert.Equal(imageUrl, createdVehicle.ImageUrl);
            Assert.Equal(wikiUrl, createdVehicle.WikiUrl);
        }
    }

    [Fact]
    public async Task Import_ExistingVehicle_UpdatesNonBlankMetadata_AccordingToPolicy()
    {
        var testDb = new CalculatorTestDatabase();
        using (testDb)
        {
            var nation = testDb.AddNation("Israel");
            var aviation = testDb.AddVehicleType("Aviation");
            var tree = testDb.AddResearchTree(nation, aviation);
            var update = testDb.AddGameUpdate("2.43");
            var draft = testDb.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);

            // Existing vehicle with initial metadata
            var initialVehicle = new Vehicle
            {
                Id = Guid.NewGuid(),
                Name = "Nesher",
                ResearchTreeId = tree.Id,
                ImageUrl = "https://example.test/old_nesher.png",
                WikiUrl = "https://example.test/old_nesher_wiki"
            };
            testDb.Context.Vehicles.Add(initialVehicle);
            await testDb.SaveChangesAsync();

            var importService = new TechTreeImportService(testDb.Context);

            var updatedImage = "https://static.encyclopedia.warthunder.com/images/nesher.png";
            var updatedWiki = "https://wiki.warthunder.com/unit/nesher";

            var csv = $"""
                VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow,AcquisitionType,FolderParentVehicleName,PrerequisiteVehicleNames,RequiredVehiclesUnlocked,ImageUrl,WikiUrl
                Nesher,7,340000,900000,0,0,Researchable,,,,{updatedImage},{updatedWiki}
                """;

            using var commitStream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var commitResult = await importService.CommitImportAsync(draft.Id, commitStream, "update_test.csv");

            Assert.True(commitResult.Success);
            Assert.Equal(0, commitResult.AutoCreatedVehiclesCount);

            testDb.Context.ChangeTracker.Clear();

            var vehicle = await testDb.Context.Vehicles.SingleAsync(v => v.Id == initialVehicle.Id);
            Assert.Equal(updatedImage, vehicle.ImageUrl);
            Assert.Equal(updatedWiki, vehicle.WikiUrl);
        }
    }

    [Fact]
    public async Task Import_BlankMetadata_DoesNotEraseExistingMetadata()
    {
        var testDb = new CalculatorTestDatabase();
        using (testDb)
        {
            var nation = testDb.AddNation("Israel");
            var aviation = testDb.AddVehicleType("Aviation");
            var tree = testDb.AddResearchTree(nation, aviation);
            var update = testDb.AddGameUpdate("2.43");
            var draft = testDb.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);

            var existingImage = "https://static.encyclopedia.warthunder.com/images/mirage_3cj.png";
            var existingWiki = "https://wiki.warthunder.com/unit/mirage_3cj";

            var vehicle = new Vehicle
            {
                Id = Guid.NewGuid(),
                Name = "Mirage IIICJ",
                ResearchTreeId = tree.Id,
                ImageUrl = existingImage,
                WikiUrl = existingWiki
            };
            testDb.Context.Vehicles.Add(vehicle);
            await testDb.SaveChangesAsync();

            var importService = new TechTreeImportService(testDb.Context);

            // Import row where ImageUrl and WikiUrl are blank
            var csv = """
                VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow,AcquisitionType,FolderParentVehicleName,PrerequisiteVehicleNames,RequiredVehiclesUnlocked,ImageUrl,WikiUrl
                Mirage IIICJ,6,220000,600000,0,0,Researchable,,,,
                """;

            using var commitStream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var commitResult = await importService.CommitImportAsync(draft.Id, commitStream, "preserve_test.csv");

            Assert.True(commitResult.Success);

            testDb.Context.ChangeTracker.Clear();

            var loadedVehicle = await testDb.Context.Vehicles.SingleAsync(v => v.Id == vehicle.Id);
            Assert.Equal(existingImage, loadedVehicle.ImageUrl);
            Assert.Equal(existingWiki, loadedVehicle.WikiUrl);
        }
    }

    [Fact]
    public async Task Import_SameNamedVehicleInAnotherResearchTree_IsNotUpdatedOrReused()
    {
        var testDb = new CalculatorTestDatabase();
        using (testDb)
        {
            var usa = testDb.AddNation("USA");
            var israel = testDb.AddNation("Israel");
            var aviation = testDb.AddVehicleType("Aviation");
            var usaTree = testDb.AddResearchTree(usa, aviation);
            var israelTree = testDb.AddResearchTree(israel, aviation);

            var update = testDb.AddGameUpdate("2.43");
            var israelDraft = testDb.AddResearchTreeVersion(israelTree, update, ResearchTreeVersionStatus.Draft);

            // Existing vehicle named "Spitfire" in USA tree
            var usaSpitfire = new Vehicle
            {
                Id = Guid.NewGuid(),
                Name = "Spitfire",
                ResearchTreeId = usaTree.Id,
                ImageUrl = "https://example.test/usa_spitfire.png",
                WikiUrl = "https://wiki.warthunder.com/usa_spitfire"
            };
            testDb.Context.Vehicles.Add(usaSpitfire);
            await testDb.SaveChangesAsync();

            var importService = new TechTreeImportService(testDb.Context);

            // Import "Spitfire" into Israel tree
            var israelImage = "https://example.test/israel_spitfire.png";
            var israelWiki = "https://wiki.warthunder.com/israel_spitfire";
            var csv = $"""
                VehicleName,RankNumber,RpCost,SlCost,TreeColumn,TreeRow,AcquisitionType,FolderParentVehicleName,PrerequisiteVehicleNames,RequiredVehiclesUnlocked,ImageUrl,WikiUrl
                Spitfire,4,40000,120000,0,0,Researchable,,,,{israelImage},{israelWiki}
                """;

            using var commitStream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var commitResult = await importService.CommitImportAsync(israelDraft.Id, commitStream, "israel_spitfire.csv");

            Assert.True(commitResult.Success);
            Assert.Equal(1, commitResult.AutoCreatedVehiclesCount);

            testDb.Context.ChangeTracker.Clear();

            // USA Spitfire must remain unaltered
            var reloadedUsaSpitfire = await testDb.Context.Vehicles.SingleAsync(v => v.Id == usaSpitfire.Id);
            Assert.Equal(usaTree.Id, reloadedUsaSpitfire.ResearchTreeId);
            Assert.Equal("https://example.test/usa_spitfire.png", reloadedUsaSpitfire.ImageUrl);
            Assert.Equal("https://wiki.warthunder.com/usa_spitfire", reloadedUsaSpitfire.WikiUrl);

            // Israel Spitfire must be a newly created vehicle scoped to Israel tree
            var israelSpitfire = await testDb.Context.Vehicles.SingleAsync(v => v.Name == "Spitfire" && v.ResearchTreeId == israelTree.Id);
            Assert.NotEqual(usaSpitfire.Id, israelSpitfire.Id);
            Assert.Equal(israelTree.Id, israelSpitfire.ResearchTreeId);
            Assert.Equal(israelImage, israelSpitfire.ImageUrl);
            Assert.Equal(israelWiki, israelSpitfire.WikiUrl);
        }
    }

    [Fact]
    public async Task ScraperOutput_RepresentativeIsraelAviationFixture_PassesImportPreview()
    {
        var testDb = new CalculatorTestDatabase();
        using (testDb)
        {
            var nation = testDb.AddNation("Israel");
            var aviation = testDb.AddVehicleType("Aviation");
            var tree = testDb.AddResearchTree(nation, aviation);
            var update = testDb.AddGameUpdate("2.43");
            var draft = testDb.AddResearchTreeVersion(tree, update, ResearchTreeVersionStatus.Draft);
            await testDb.SaveChangesAsync();

            var importService = new TechTreeImportService(testDb.Context);

            // Test 1: Canonical CSV fixture
            var csvFixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "wt_israel_aviation.csv");
            if (!File.Exists(csvFixturePath))
            {
                // Fallback to local path if running from repo root
                csvFixturePath = Path.Combine(Directory.GetCurrentDirectory(), "Fixtures", "wt_israel_aviation.csv");
            }

            Assert.True(File.Exists(csvFixturePath), $"Fixture not found at {csvFixturePath}");

            await using (var csvStream = File.OpenRead(csvFixturePath))
            {
                var csvPreview = await importService.PreviewImportAsync(draft.Id, csvStream, "wt_israel_aviation.csv");
                Assert.True(csvPreview.IsValid, $"CSV Preview failed with errors: {string.Join(", ", csvPreview.Errors)}");
                Assert.Empty(csvPreview.Errors);
                Assert.Equal(49, csvPreview.TotalRowsDetected);
                Assert.Equal(49, csvPreview.NewEntriesCount);
                Assert.Equal(49, csvPreview.AutoCreatedVehiclesCount);
            }

            // Test 2: Multi-sheet XLSX fixture (sheet 1 Import)
            var xlsxFixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "wt_israel_aviation.xlsx");
            if (!File.Exists(xlsxFixturePath))
            {
                xlsxFixturePath = Path.Combine(Directory.GetCurrentDirectory(), "Fixtures", "wt_israel_aviation.xlsx");
            }

            Assert.True(File.Exists(xlsxFixturePath), $"Fixture not found at {xlsxFixturePath}");

            await using (var xlsxStream = File.OpenRead(xlsxFixturePath))
            {
                var xlsxPreview = await importService.PreviewImportAsync(draft.Id, xlsxStream, "wt_israel_aviation.xlsx");
                Assert.True(xlsxPreview.IsValid, $"XLSX Preview failed with errors: {string.Join(", ", xlsxPreview.Errors)}");
                Assert.Empty(xlsxPreview.Errors);
                Assert.Equal(49, xlsxPreview.TotalRowsDetected);
                Assert.Equal(49, xlsxPreview.NewEntriesCount);
                Assert.Equal(49, xlsxPreview.AutoCreatedVehiclesCount);
            }
        }
    }
}
