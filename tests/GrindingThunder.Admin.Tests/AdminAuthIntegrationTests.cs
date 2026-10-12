using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GrindingThunder.Admin.Tests;

public class AdminAuthIntegrationTests : IClassFixture<AdminWebApplicationFactory>
{
    private readonly AdminWebApplicationFactory _factory;

    public AdminAuthIntegrationTests(AdminWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string GetLocationPath(Uri? location)
    {
        if (location == null) return string.Empty;
        return location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Catalog")]
    [InlineData("/Catalog/Nations")]
    [InlineData("/Catalog/Nations/Create")]
    [InlineData("/Trees")]
    [InlineData("/Import")]
    public async Task Unauthenticated_Access_To_Protected_Pages_Redirects_To_Login(string path)
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Act
        var response = await client.GetAsync(path);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.StartsWith("/Login", GetLocationPath(response.Headers.Location), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_With_Invalid_Credentials_Displays_Error_Feedback()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var getLoginResponse = await client.GetAsync("/Login");
        Assert.True(getLoginResponse.IsSuccessStatusCode, await getLoginResponse.Content.ReadAsStringAsync());
        var antiforgeryToken = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(getLoginResponse);

        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Username"] = "wronguser",
            ["Input.Password"] = "wrongpassword",
            ["__RequestVerificationToken"] = antiforgeryToken
        });

        // Act
        var postResponse = await client.PostAsync("/Login", formContent);

        // Assert
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var html = await postResponse.Content.ReadAsStringAsync();
        Assert.Contains("Invalid username or password.", html);
    }

    [Fact]
    public async Task Login_With_Valid_Credentials_Establishes_Cookie_Session_And_Redirects_To_Dashboard()
    {
        // Arrange
        _factory.MockApiHandler.RecordedRequests.Clear();

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var getLoginResponse = await client.GetAsync("/Login");
        Assert.True(getLoginResponse.IsSuccessStatusCode, await getLoginResponse.Content.ReadAsStringAsync());
        var antiforgeryToken = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(getLoginResponse);

        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Username"] = "admin",
            ["Input.Password"] = "admin123",
            ["__RequestVerificationToken"] = antiforgeryToken
        });

        // Act - Submit login form
        var postResponse = await client.PostAsync("/Login", formContent);

        // Assert - Redirect to dashboard
        Assert.Equal(HttpStatusCode.Redirect, postResponse.StatusCode);
        Assert.NotNull(postResponse.Headers.Location);
        Assert.True(postResponse.Headers.Location.OriginalString == "/" || postResponse.Headers.Location.OriginalString.Contains("Index"));

        // Act - Visit dashboard with authenticated session cookie
        var dashboardResponse = await client.GetAsync("/");

        // Assert - Dashboard renders successfully with live API status and user details
        var html = await dashboardResponse.Content.ReadAsStringAsync();
        Assert.True(dashboardResponse.IsSuccessStatusCode, html);
        Assert.Contains("Admin Dashboard", html);
        Assert.Contains("ONLINE", html);
        Assert.Contains("admin", html);
        Assert.Contains("Admin", html);

        // Verify that the typed ApiClient communicated with GET /api/auth/me and propagated the Bearer token
        var meRequests = _factory.MockApiHandler.RecordedRequests
            .Where(r => r.RequestUri != null && r.RequestUri.AbsolutePath.EndsWith("/api/auth/me"))
            .ToList();

        Assert.NotEmpty(meRequests);
        var lastMeRequest = meRequests.Last();
        Assert.NotNull(lastMeRequest.Headers.Authorization);
        Assert.Equal("Bearer", lastMeRequest.Headers.Authorization.Scheme);
        Assert.Equal("test-jwt-token-xyz", lastMeRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Logout_Clears_Session_And_Redirects_To_Login()
    {
        // Arrange - Log in first
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var getLoginResponse = await client.GetAsync("/Login");
        var antiforgeryToken = await AdminWebApplicationFactory.ExtractAntiforgeryTokenAsync(getLoginResponse);

        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Username"] = "admin",
            ["Input.Password"] = "admin123",
            ["__RequestVerificationToken"] = antiforgeryToken
        });
        var loginResponse = await client.PostAsync("/Login", formContent);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

        // Act - Trigger Logout
        var logoutResponse = await client.PostAsync("/Logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiforgeryToken
        }));

        // Assert - Redirected to /Login
        Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);
        Assert.NotNull(logoutResponse.Headers.Location);
        Assert.StartsWith("/Login", GetLocationPath(logoutResponse.Headers.Location), StringComparison.OrdinalIgnoreCase);

        // Subsequent protected request should be challenged with redirect to /Login
        var protectedResponse = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, protectedResponse.StatusCode);
        Assert.StartsWith("/Login", GetLocationPath(protectedResponse.Headers.Location), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccessDenied_Page_Renders_Successfully()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/AccessDenied");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Access Denied", html);
    }
}
