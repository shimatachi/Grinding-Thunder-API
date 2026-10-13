using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GrindingThunder.Admin.Tests;

/// <summary>
/// Integration coverage of the Admin-only account management pages:
/// authorization, API error feedback, and cookie-to-JWT propagation.
/// </summary>
public class AccountManagementTests : IClassFixture<AdminWebApplicationFactory>
{
    private readonly AdminWebApplicationFactory _factory;

    public AccountManagementTests(AdminWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    private static async Task SignInAsync(HttpClient client, string username, string password)
    {
        var getLogin = await client.GetAsync("/Login");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(getLogin);
        var response = await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Username"] = username,
            ["Input.Password"] = password,
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static string PathOf(Uri? location) =>
        location is null ? string.Empty : location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;

    [Theory]
    [InlineData("/Accounts")]
    [InlineData("/Accounts/Create")]
    public async Task Unauthenticated_AccountPages_RedirectToLogin(string path)
    {
        var client = CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Login", PathOf(response.Headers.Location), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unauthenticated_EditPage_RedirectsToLogin()
    {
        var client = CreateClient();

        var response = await client.GetAsync($"/Accounts/{TestMockApiHandler.AdminAccountId}/Edit");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Login", PathOf(response.Headers.Location), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("editor", "editor123")]
    [InlineData("publisher", "publisher123")]
    public async Task NonAdmin_AccountPages_RedirectToAccessDenied(string username, string password)
    {
        var client = CreateClient();
        await SignInAsync(client, username, password);

        var listResponse = await client.GetAsync("/Accounts");
        Assert.Equal(HttpStatusCode.Redirect, listResponse.StatusCode);
        Assert.StartsWith("/AccessDenied", PathOf(listResponse.Headers.Location), StringComparison.OrdinalIgnoreCase);

        var createResponse = await client.GetAsync("/Accounts/Create");
        Assert.Equal(HttpStatusCode.Redirect, createResponse.StatusCode);
        Assert.StartsWith("/AccessDenied", PathOf(createResponse.Headers.Location), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_AccountList_RendersAccountsFromApi()
    {
        var client = CreateClient();
        await SignInAsync(client, "admin", "admin123");

        var response = await client.GetAsync("/Accounts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(">admin<", html);
        Assert.Contains(">editor<", html);
        Assert.Contains("Active", html);
    }

    [Fact]
    public async Task Admin_CreateAccount_PostsToApiWithBearerToken()
    {
        _factory.MockApiHandler.RecordedRequests.Clear();
        var client = CreateClient();
        await SignInAsync(client, "admin", "admin123");

        var getCreate = await client.GetAsync("/Accounts/Create");
        Assert.Equal(HttpStatusCode.OK, getCreate.StatusCode);
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(getCreate);

        var postResponse = await client.PostAsync("/Accounts/Create", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Input.Username", "newpublisher"),
            new KeyValuePair<string, string>("Input.Password", "PublisherPass99!"),
            new KeyValuePair<string, string>("Input.Roles", "Publisher"),
            new KeyValuePair<string, string>("Input.Roles", "Editor"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]));

        Assert.Equal(HttpStatusCode.Redirect, postResponse.StatusCode);
        Assert.StartsWith("/Accounts", PathOf(postResponse.Headers.Location), StringComparison.OrdinalIgnoreCase);

        var createRequests = _factory.MockApiHandler.RecordedRequests
            .Where(r => r.Method == HttpMethod.Post &&
                        r.RequestUri!.AbsolutePath.EndsWith("/api/admin/accounts", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var request = Assert.Single(createRequests);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("test-jwt-token-xyz", request.Headers.Authorization?.Parameter);
        var body = await request.Content!.ReadAsStringAsync();
        Assert.Contains("newpublisher", body);
        Assert.Contains("Publisher", body);
    }

    [Fact]
    public async Task Admin_CreateAccount_ApiConflict_ShowsErrorFeedback()
    {
        _factory.MockApiHandler.ConflictNextAccountMutation = true;
        var client = CreateClient();
        await SignInAsync(client, "admin", "admin123");

        var getCreate = await client.GetAsync("/Accounts/Create");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(getCreate);

        var postResponse = await client.PostAsync("/Accounts/Create", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Input.Username", "admin"),
            new KeyValuePair<string, string>("Input.Password", "AdminPass123!"),
            new KeyValuePair<string, string>("Input.Roles", "Admin"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]));

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var html = await postResponse.Content.ReadAsStringAsync();
        Assert.Contains("Username is already taken.", html);
    }

    [Fact]
    public async Task Admin_EditAccount_RendersEditPage()
    {
        _factory.MockApiHandler.RecordedRequests.Clear();
        var client = CreateClient();
        await SignInAsync(client, "admin", "admin123");

        var response = await client.GetAsync($"/Accounts/{TestMockApiHandler.AdminAccountId}/Edit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Edit Account", html);
        Assert.Contains("value=\"admin\"", html);
        Assert.Contains("Replace password", html);

        var getAccountRequests = _factory.MockApiHandler.RecordedRequests
            .Where(r => r.Method == HttpMethod.Get &&
                        r.RequestUri!.AbsolutePath.Equals($"/api/admin/accounts/{TestMockApiHandler.AdminAccountId}", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var request = Assert.Single(getAccountRequests);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("test-jwt-token-xyz", request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task Admin_UpdateAccount_PostsToApiWithBearerTokenAndRequestBody()
    {
        _factory.MockApiHandler.RecordedRequests.Clear();
        var client = CreateClient();
        await SignInAsync(client, "admin", "admin123");

        var getEdit = await client.GetAsync($"/Accounts/{TestMockApiHandler.AdminAccountId}/Edit");
        Assert.Equal(HttpStatusCode.OK, getEdit.StatusCode);
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(getEdit);

        var postResponse = await client.PostAsync($"/Accounts/{TestMockApiHandler.AdminAccountId}/Edit", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Input.Username", "updatedadmin"),
            new KeyValuePair<string, string>("Input.Roles", "Admin"),
            new KeyValuePair<string, string>("Input.Roles", "Publisher"),
            new KeyValuePair<string, string>("Input.IsActive", "true"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]));

        Assert.Equal(HttpStatusCode.Redirect, postResponse.StatusCode);
        Assert.StartsWith("/Accounts", PathOf(postResponse.Headers.Location), StringComparison.OrdinalIgnoreCase);

        var putRequests = _factory.MockApiHandler.RecordedRequests
            .Where(r => r.Method == HttpMethod.Put &&
                        r.RequestUri!.AbsolutePath.Equals($"/api/admin/accounts/{TestMockApiHandler.AdminAccountId}", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var request = Assert.Single(putRequests);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("test-jwt-token-xyz", request.Headers.Authorization?.Parameter);
        var body = await request.Content!.ReadAsStringAsync();
        Assert.Contains("updatedadmin", body);
        Assert.Contains("Admin", body);
        Assert.Contains("Publisher", body);
        Assert.Contains("true", body);
    }

    [Fact]
    public async Task Admin_UpdateAccount_ApiConflict_ShowsErrorFeedback()
    {
        _factory.MockApiHandler.ConflictNextAccountMutation = true;
        var client = CreateClient();
        await SignInAsync(client, "admin", "admin123");

        var getEdit = await client.GetAsync($"/Accounts/{TestMockApiHandler.AdminAccountId}/Edit");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(getEdit);

        var postResponse = await client.PostAsync($"/Accounts/{TestMockApiHandler.AdminAccountId}/Edit", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Input.Username", "admin"),
            new KeyValuePair<string, string>("Input.Roles", "Editor"),
            new KeyValuePair<string, string>("Input.IsActive", "true"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]));

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var html = await postResponse.Content.ReadAsStringAsync();
        Assert.Contains("This change would leave the system without an active Admin account.", html);
    }

    [Fact]
    public async Task Admin_ReplacePassword_PostsToApiWithBearerTokenAndRequestBody()
    {
        _factory.MockApiHandler.RecordedRequests.Clear();
        var client = CreateClient();
        await SignInAsync(client, "admin", "admin123");

        var getEdit = await client.GetAsync($"/Accounts/{TestMockApiHandler.AdminAccountId}/Edit");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(getEdit);

        var postResponse = await client.PostAsync($"/Accounts/{TestMockApiHandler.AdminAccountId}/Edit?handler=Password", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("PasswordInput.Password", "NewStrongPassword123!"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]));

        Assert.Equal(HttpStatusCode.Redirect, postResponse.StatusCode);
        Assert.StartsWith("/Accounts", PathOf(postResponse.Headers.Location), StringComparison.OrdinalIgnoreCase);

        var passwordRequests = _factory.MockApiHandler.RecordedRequests
            .Where(r => r.Method == HttpMethod.Put &&
                        r.RequestUri!.AbsolutePath.Equals($"/api/admin/accounts/{TestMockApiHandler.AdminAccountId}/password", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var request = Assert.Single(passwordRequests);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("test-jwt-token-xyz", request.Headers.Authorization?.Parameter);
        var body = await request.Content!.ReadAsStringAsync();
        Assert.Contains("NewStrongPassword123!", body);
    }

    [Theory]
    [InlineData("editor", "editor123")]
    [InlineData("publisher", "publisher123")]
    public async Task NonAdmin_EditPageAndPostHandlers_RedirectToAccessDenied(string username, string password)
    {
        var client = CreateClient();
        await SignInAsync(client, username, password);

        var getResponse = await client.GetAsync($"/Accounts/{TestMockApiHandler.AdminAccountId}/Edit");
        Assert.Equal(HttpStatusCode.Redirect, getResponse.StatusCode);
        Assert.StartsWith("/AccessDenied", PathOf(getResponse.Headers.Location), StringComparison.OrdinalIgnoreCase);

        var getRoot = await client.GetAsync("/");
        var token = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(getRoot);

        var postUpdateResponse = await client.PostAsync($"/Accounts/{TestMockApiHandler.AdminAccountId}/Edit", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Input.Username", "hackedname"),
            new KeyValuePair<string, string>("Input.Roles", "Admin"),
            new KeyValuePair<string, string>("Input.IsActive", "true"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]));
        Assert.Equal(HttpStatusCode.Redirect, postUpdateResponse.StatusCode);
        Assert.StartsWith("/AccessDenied", PathOf(postUpdateResponse.Headers.Location), StringComparison.OrdinalIgnoreCase);

        var postPasswordResponse = await client.PostAsync($"/Accounts/{TestMockApiHandler.AdminAccountId}/Edit?handler=Password", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("PasswordInput.Password", "HackedPassword123!"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]));
        Assert.Equal(HttpStatusCode.Redirect, postPasswordResponse.StatusCode);
        Assert.StartsWith("/AccessDenied", PathOf(postPasswordResponse.Headers.Location), StringComparison.OrdinalIgnoreCase);
    }
}
