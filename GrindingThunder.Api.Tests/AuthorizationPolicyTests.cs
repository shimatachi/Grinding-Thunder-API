using System.Security.Claims;
using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class AuthorizationPolicyTests
{
    private readonly IAuthorizationService _authService;

    public AuthorizationPolicyTests()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var env = new TestHostEnvironment { EnvironmentName = Environments.Development };

        services.AddLogging();
        services.AddAdminSecurity(config, env);

        var provider = services.BuildServiceProvider();
        _authService = provider.GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal CreatePrincipal(params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "testuser")
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, "TestAuthType");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task RequireEditorRole_AllowedForEditorAndAdmin_ForbiddenForPublisher()
    {
        var editor = CreatePrincipal(AuthRoles.Editor);
        var admin = CreatePrincipal(AuthRoles.Admin);
        var publisher = CreatePrincipal(AuthRoles.Publisher);

        var editorResult = await _authService.AuthorizeAsync(editor, AuthPolicies.RequireEditorRole);
        var adminResult = await _authService.AuthorizeAsync(admin, AuthPolicies.RequireEditorRole);
        var publisherResult = await _authService.AuthorizeAsync(publisher, AuthPolicies.RequireEditorRole);

        Assert.True(editorResult.Succeeded);
        Assert.True(adminResult.Succeeded);
        Assert.False(publisherResult.Succeeded);
    }

    [Fact]
    public async Task RequirePublisherRole_AllowedForPublisherAndAdmin_ForbiddenForEditor()
    {
        var publisher = CreatePrincipal(AuthRoles.Publisher);
        var admin = CreatePrincipal(AuthRoles.Admin);
        var editor = CreatePrincipal(AuthRoles.Editor);

        var publisherResult = await _authService.AuthorizeAsync(publisher, AuthPolicies.RequirePublisherRole);
        var adminResult = await _authService.AuthorizeAsync(admin, AuthPolicies.RequirePublisherRole);
        var editorResult = await _authService.AuthorizeAsync(editor, AuthPolicies.RequirePublisherRole);

        Assert.True(publisherResult.Succeeded);
        Assert.True(adminResult.Succeeded);
        Assert.False(editorResult.Succeeded);
    }

    [Fact]
    public async Task RequireAdminRole_AllowedForAdminOnly_ForbiddenForEditorAndPublisher()
    {
        var admin = CreatePrincipal(AuthRoles.Admin);
        var editor = CreatePrincipal(AuthRoles.Editor);
        var publisher = CreatePrincipal(AuthRoles.Publisher);

        var adminResult = await _authService.AuthorizeAsync(admin, AuthPolicies.RequireAdminRole);
        var editorResult = await _authService.AuthorizeAsync(editor, AuthPolicies.RequireAdminRole);
        var publisherResult = await _authService.AuthorizeAsync(publisher, AuthPolicies.RequireAdminRole);

        Assert.True(adminResult.Succeeded);
        Assert.False(editorResult.Succeeded);
        Assert.False(publisherResult.Succeeded);
    }

    [Fact]
    public async Task AllPolicies_UnauthenticatedPrincipal_Fails()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.False((await _authService.AuthorizeAsync(anonymous, AuthPolicies.RequireEditorRole)).Succeeded);
        Assert.False((await _authService.AuthorizeAsync(anonymous, AuthPolicies.RequirePublisherRole)).Succeeded);
        Assert.False((await _authService.AuthorizeAsync(anonymous, AuthPolicies.RequireAdminRole)).Succeeded);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "GrindingThunder.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

