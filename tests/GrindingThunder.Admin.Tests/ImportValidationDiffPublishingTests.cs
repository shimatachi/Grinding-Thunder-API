using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GrindingThunder.Admin.Tests;

public class ImportValidationDiffPublishingTests
{
    [Fact]
    public async Task ImportPreviewAndCommit_UseMultipartAndReturnToDraft()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(Options());
        await LoginAsync(client, "editor");
        var page = await client.GetAsync($"/Import?versionId={TestMockApiHandler.DraftVersionId}");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);
        using var upload = new MultipartFormDataContent();
        upload.Add(new StringContent(token), "__RequestVerificationToken");
        upload.Add(new StringContent(TestMockApiHandler.DraftVersionId.ToString()), "versionId");
        upload.Add(new StringContent("true"), "autoCreateMissingVehicles");
        upload.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("VehicleName,RankNumber\nF-16A,1")), "file", "tree.csv");
        var preview = await client.PostAsync("/Import?handler=Preview", upload);
        var html = await preview.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Contains("Rows: <strong>2</strong>", html);
        Assert.Contains("Vehicles to create: <strong>1</strong>", html);
        Assert.Contains("Ranks to create: <strong>1</strong>", html);
        Assert.Contains("One vehicle will be created.", html);
        Assert.Contains("Commit import", html);
        var previewToken = Regex.Match(html, "name=\"previewToken\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(previewToken);
        token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(preview);
        var commit = await client.PostAsync("/Import?handler=Commit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["versionId"] = TestMockApiHandler.DraftVersionId.ToString(),
            ["previewToken"] = previewToken,
            ["__RequestVerificationToken"] = token
        }));
        html = await commit.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);
        Assert.Contains("Imported 2 entries", html);
        Assert.Contains("Back to draft editor", html);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request => request.RequestUri?.AbsolutePath?.EndsWith("/import/preview") == true && request.Content is MultipartFormDataContent);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request => request.RequestUri?.AbsolutePath?.EndsWith("/import") == true && request.Content is MultipartFormDataContent);
    }

    [Fact]
    public async Task InvalidPreview_ShowsDiagnosticsAndBlocksCommit()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(Options());
        await LoginAsync(client, "editor");
        factory.MockApiHandler.InvalidNextImportPreview = true;
        var page = await client.GetAsync("/Import");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);
        using var upload = new MultipartFormDataContent();
        upload.Add(new StringContent(token), "__RequestVerificationToken");
        upload.Add(new StringContent(TestMockApiHandler.DraftVersionId.ToString()), "versionId");
        upload.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("x")), "file", "tree.tsv");
        var response = await client.PostAsync("/Import?handler=Preview", upload);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Coordinate collision", html);
        Assert.DoesNotContain("Commit import", html);
    }

    [Fact]
    public async Task Publisher_CannotAccessImport_OrInvokeImportApi()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(Options());
        await LoginAsync(client, "publisher");
        var get = await client.GetAsync("/Import");
        Assert.Equal(HttpStatusCode.Redirect, get.StatusCode);
        Assert.Contains("AccessDenied", get.Headers.Location?.OriginalString);
        var post = await client.PostAsync("/Import?handler=Commit", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.DoesNotContain(factory.MockApiHandler.RecordedRequests, request => request.RequestUri?.AbsolutePath?.Contains("/import") == true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Validation_ShowsStatusAndDiagnostics(bool invalid)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(Options());
        await LoginAsync(client, "publisher");
        factory.MockApiHandler.InvalidNextValidation = invalid;
        var response = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=Validate");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(invalid ? "Validation failed" : "Validation passed", html);
        Assert.Contains(invalid ? "Prerequisite cycle" : "Check folder placement", html);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request => request.RequestUri?.AbsolutePath?.EndsWith("/validate") == true);
    }

    [Fact]
    public async Task Diff_RendersEconomicVehiclePrerequisiteAndRankChanges()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(Options());
        await LoginAsync(client, "publisher");
        var response = await client.GetAsync($"/Trees/Diff?researchTreeId={TestMockApiHandler.ResearchTreeId}&sourceVersionId={TestMockApiHandler.PublishedVersionId}&targetVersionId={TestMockApiHandler.DraftVersionId}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Net RP delta", html);
        Assert.Contains("10000", html);
        Assert.Contains("F-16A", html);
        Assert.Contains("F-4E", html);
        Assert.Contains("Prerequisite changes", html);
        Assert.Contains("Rank gate changes", html);
        Assert.Contains(factory.MockApiHandler.RecordedRequests, request => request.RequestUri?.Query?.Contains("sourceVersionId=") == true);
    }

    [Theory]
    [InlineData("publisher")]
    [InlineData("admin")]
    public async Task PublisherAndAdmin_CanPublishConfirmedDraft(string username)
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(Options());
        await LoginAsync(client, username);
        var page = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Publish Version", html);
        Assert.Contains("immutable historical snapshot", html);
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);
        var post = await client.PostAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=Publish", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["confirmImmutable"] = "true", ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        var published = await client.GetAsync(post.Headers.Location!);
        html = await published.Content.ReadAsStringAsync();
        Assert.Contains("Version published", html);
        Assert.Contains("published and immutable", html);
        Assert.DoesNotContain("name=\"confirmImmutable\"", html);
        Assert.True(factory.MockApiHandler.DraftPublished);
    }

    [Fact]
    public async Task Editor_CannotPublishEvenWithDirectPost()
    {
        using var factory = new AdminWebApplicationFactory();
        using var client = factory.CreateClient(Options());
        await LoginAsync(client, "editor");
        var page = await client.GetAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}");
        var html = await page.Content.ReadAsStringAsync();
        Assert.DoesNotContain("name=\"confirmImmutable\"", html);
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);
        var post = await client.PostAsync($"/Trees/Version/{TestMockApiHandler.DraftVersionId}?handler=Publish", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["confirmImmutable"] = "true", ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Contains("AccessDenied", post.Headers.Location?.OriginalString);
        Assert.DoesNotContain(factory.MockApiHandler.RecordedRequests, request => request.RequestUri?.AbsolutePath?.EndsWith("/publish") == true);
    }

    private static WebApplicationFactoryClientOptions Options() => new() { AllowAutoRedirect = false, HandleCookies = true };

    private static async Task LoginAsync(HttpClient client, string username)
    {
        var page = await client.GetAsync("/Login");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(page);
        var response = await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Username"] = username, ["Input.Password"] = $"{username}123", ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
