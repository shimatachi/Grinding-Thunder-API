using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Application.Security;
using Xunit;

namespace GrindingThunder.Api.Tests;

/// <summary>
/// HTTP-level coverage of the Admin-only account lifecycle endpoints and the
/// token/session behavior after disablement, password replacement, username
/// changes, and role changes.
/// </summary>
public class AdminAccountsIntegrationTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminAccountsIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync() => await _factory.InitializeDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return request;
    }

    private Task<string> AdminTokenAsync() =>
        _factory.GetTokenAsync("admin", "AdminPass123!");

    private async Task<AdminAccountDto> CreateAccountAsync(
        string token, string username, string password, params string[] roles)
    {
        using var request = Authorized(HttpMethod.Post, "/api/admin/accounts", token,
            new CreateAdminAccountRequest(username, password, roles));
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = await response.Content.ReadFromJsonAsync<AdminAccountDto>();
        Assert.NotNull(account);
        return account;
    }

    [Fact]
    public async Task Accounts_WithoutToken_Return401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/admin/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/admin/accounts",
            new CreateAdminAccountRequest("x", "Password123!", [AuthRoles.Admin]))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PutAsJsonAsync($"/api/admin/accounts/{Guid.NewGuid()}",
            new UpdateAdminAccountRequest("x", [AuthRoles.Admin], true))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PutAsJsonAsync($"/api/admin/accounts/{Guid.NewGuid()}/password",
            new ReplaceAdminPasswordRequest("Password123!"))).StatusCode);
    }

    [Theory]
    [InlineData("editor", "EditorPass123!")]
    [InlineData("publisher", "PublisherPass123!")]
    public async Task Accounts_NonAdminRoles_Return403(string username, string password)
    {
        var token = await _factory.GetTokenAsync(username, password);

        using var listRequest = Authorized(HttpMethod.Get, "/api/admin/accounts", token);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(listRequest)).StatusCode);

        using var createRequest = Authorized(HttpMethod.Post, "/api/admin/accounts", token,
            new CreateAdminAccountRequest("x", "Password123!", [AuthRoles.Editor]));
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(createRequest)).StatusCode);
    }

    [Fact]
    public async Task List_AsAdmin_ReturnsSeededAccounts_WithoutPasswordHashes()
    {
        var token = await AdminTokenAsync();
        using var request = Authorized(HttpMethod.Get, "/api/admin/accounts", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("asswordHash", raw);
        Assert.DoesNotContain("AdminPass123!", raw);

        var accounts = await response.Content.ReadFromJsonAsync<List<AdminAccountDto>>();
        Assert.NotNull(accounts);
        Assert.Contains(accounts, a => a.Username == "admin" && a.Roles.Contains(AuthRoles.Admin) && a.IsActive);
        Assert.Contains(accounts, a => a.Username == "editor" && a.Roles.Contains(AuthRoles.Editor));
        Assert.Contains(accounts, a => a.Username == "publisher" && a.Roles.Contains(AuthRoles.Publisher));
    }

    [Fact]
    public async Task Create_ValidAccount_CanLoginImmediately()
    {
        var token = await AdminTokenAsync();
        var account = await CreateAccountAsync(token, "neweditor", "NewEditorPass1!", AuthRoles.Editor);

        Assert.Equal("neweditor", account.Username);
        Assert.True(account.IsActive);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("NEWEDITOR", "NewEditorPass1!"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Create_NormalizedDuplicate_Returns409()
    {
        var token = await AdminTokenAsync();
        await CreateAccountAsync(token, "dupuser", "Password123!", AuthRoles.Editor);

        using var request = Authorized(HttpMethod.Post, "/api/admin/accounts", token,
            new CreateAdminAccountRequest("DUPUSER", "Password123!", [AuthRoles.Editor]));
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("", "Password123!", new[] { "Editor" })]
    [InlineData("shortpw", "abc", new[] { "Editor" })]
    [InlineData("noroles", "Password123!", new string[0])]
    [InlineData("badrole", "Password123!", new[] { "SuperUser" })]
    public async Task Create_InvalidInput_Returns400(string username, string password, string[] roles)
    {
        var token = await AdminTokenAsync();
        using var request = Authorized(HttpMethod.Post, "/api/admin/accounts", token,
            new CreateAdminAccountRequest(username, password, roles));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DisabledAccount_LoginRejected_AndExistingTokenLosesAccess()
    {
        var token = await AdminTokenAsync();
        var account = await CreateAccountAsync(token, "tobedisabled", "Password123!", AuthRoles.Editor);
        var victimToken = await _factory.GetTokenAsync("tobedisabled", "Password123!");

        using var disableRequest = Authorized(HttpMethod.Put, $"/api/admin/accounts/{account.Id}", token,
            new UpdateAdminAccountRequest("tobedisabled", [AuthRoles.Editor], IsActive: false));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(disableRequest)).StatusCode);

        // Superseded token loses secured API access immediately.
        using var meRequest = Authorized(HttpMethod.Get, "/api/auth/me", victimToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(meRequest)).StatusCode);

        // Disabled account can no longer authenticate.
        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("tobedisabled", "Password123!"));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task ReplacePassword_InvalidatesOldToken_AndOldPassword()
    {
        var token = await AdminTokenAsync();
        var account = await CreateAccountAsync(token, "pwchange", "OriginalPass1!", AuthRoles.Editor);
        var oldToken = await _factory.GetTokenAsync("pwchange", "OriginalPass1!");

        using var replaceRequest = Authorized(HttpMethod.Put, $"/api/admin/accounts/{account.Id}/password", token,
            new ReplaceAdminPasswordRequest("Replacement2!"));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(replaceRequest)).StatusCode);

        using var meRequest = Authorized(HttpMethod.Get, "/api/auth/me", oldToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(meRequest)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("pwchange", "OriginalPass1!"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("pwchange", "Replacement2!"))).StatusCode);
    }

    [Fact]
    public async Task RoleChange_InvalidatesOldToken_AndNewTokenCarriesNewRoles()
    {
        var token = await AdminTokenAsync();
        var account = await CreateAccountAsync(token, "roleflip", "Password123!", AuthRoles.Admin);
        var oldToken = await _factory.GetTokenAsync("roleflip", "Password123!");

        using var demoteRequest = Authorized(HttpMethod.Put, $"/api/admin/accounts/{account.Id}", token,
            new UpdateAdminAccountRequest("roleflip", [AuthRoles.Editor], IsActive: true));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(demoteRequest)).StatusCode);

        // Token issued before the role change is rejected.
        using var staleRequest = Authorized(HttpMethod.Get, "/api/auth/admin-check", oldToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(staleRequest)).StatusCode);

        // A fresh token carries the demoted roles: Admin policy now forbids it,
        // while the Editor policy still grants access.
        var newToken = await _factory.GetTokenAsync("roleflip", "Password123!");
        using var adminCheck = Authorized(HttpMethod.Get, "/api/auth/admin-check", newToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(adminCheck)).StatusCode);
        using var editorCheck = Authorized(HttpMethod.Get, "/api/auth/editor-check", newToken);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(editorCheck)).StatusCode);
    }

    [Fact]
    public async Task UsernameChange_InvalidatesOldToken_AndNewUsernameLogsIn()
    {
        var token = await AdminTokenAsync();
        var account = await CreateAccountAsync(token, "before-rename", "Password123!", AuthRoles.Editor);
        var oldToken = await _factory.GetTokenAsync("before-rename", "Password123!");

        using var renameRequest = Authorized(HttpMethod.Put, $"/api/admin/accounts/{account.Id}", token,
            new UpdateAdminAccountRequest("after-rename", [AuthRoles.Editor], IsActive: true));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(renameRequest)).StatusCode);

        using var meRequest = Authorized(HttpMethod.Get, "/api/auth/me", oldToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(meRequest)).StatusCode);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("After-RENAME", "Password123!"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var payload = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal("after-rename", payload!.Username);
    }

    [Fact]
    public async Task Update_DuplicateUsername_Returns409()
    {
        var token = await AdminTokenAsync();
        await CreateAccountAsync(token, "existingname", "Password123!", AuthRoles.Editor);
        var second = await CreateAccountAsync(token, "renamecandidate", "Password123!", AuthRoles.Editor);

        using var request = Authorized(HttpMethod.Put, $"/api/admin/accounts/{second.Id}", token,
            new UpdateAdminAccountRequest("EXISTINGNAME", [AuthRoles.Editor], IsActive: true));
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_UnknownAccount_Returns404()
    {
        var token = await AdminTokenAsync();

        using var updateRequest = Authorized(HttpMethod.Put, $"/api/admin/accounts/{Guid.NewGuid()}", token,
            new UpdateAdminAccountRequest("ghost", [AuthRoles.Editor], IsActive: true));
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendAsync(updateRequest)).StatusCode);

        using var passwordRequest = Authorized(HttpMethod.Put, $"/api/admin/accounts/{Guid.NewGuid()}/password", token,
            new ReplaceAdminPasswordRequest("Password123!"));
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendAsync(passwordRequest)).StatusCode);
    }

    [Fact]
    public async Task Update_LastActiveAdmin_DisableOrDemote_Returns409()
    {
        var token = await AdminTokenAsync();
        using var listRequest = Authorized(HttpMethod.Get, "/api/admin/accounts", token);
        var accounts = await (await _client.SendAsync(listRequest)).Content.ReadFromJsonAsync<List<AdminAccountDto>>();
        var adminAccount = accounts!.Single(a => a.Username == "admin");

        using var disableRequest = Authorized(HttpMethod.Put, $"/api/admin/accounts/{adminAccount.Id}", token,
            new UpdateAdminAccountRequest("admin", [AuthRoles.Admin], IsActive: false));
        Assert.Equal(HttpStatusCode.Conflict, (await _client.SendAsync(disableRequest)).StatusCode);

        using var demoteRequest = Authorized(HttpMethod.Put, $"/api/admin/accounts/{adminAccount.Id}", token,
            new UpdateAdminAccountRequest("admin", [AuthRoles.Editor], IsActive: true));
        Assert.Equal(HttpStatusCode.Conflict, (await _client.SendAsync(demoteRequest)).StatusCode);

        // The seeded Admin account remains fully functional.
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", "AdminPass123!"))).StatusCode);
    }
}
