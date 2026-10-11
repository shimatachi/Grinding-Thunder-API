using System.Net;
using System.Security.Claims;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GrindingThunder.Admin.Tests;

public class TokenPropagationTests
{
    private class DummyInnerHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task SendAsync_WhenAuthenticatedWithTokenClaim_AttachesBearerToken()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "testadmin"),
            new Claim("access_token", "jwt-secret-abc-123")
        };
        var identity = new ClaimsIdentity(claims, "Cookies");
        httpContext.User = new ClaimsPrincipal(identity);

        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
        var innerHandler = new DummyInnerHandler();
        var handler = new BearerTokenHandler(httpContextAccessor)
        {
            InnerHandler = innerHandler
        };

        var invoker = new HttpMessageInvoker(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost:5034/api/auth/me");

        // Act
        var response = await invoker.SendAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(innerHandler.LastRequest);
        Assert.NotNull(innerHandler.LastRequest.Headers.Authorization);
        Assert.Equal("Bearer", innerHandler.LastRequest.Headers.Authorization.Scheme);
        Assert.Equal("jwt-secret-abc-123", innerHandler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task SendAsync_WhenUnauthenticated_DoesNotAttachAuthorizationHeader()
    {
        // Arrange
        var httpContext = new DefaultHttpContext(); // No user / unauthenticated
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
        var innerHandler = new DummyInnerHandler();
        var handler = new BearerTokenHandler(httpContextAccessor)
        {
            InnerHandler = innerHandler
        };

        var invoker = new HttpMessageInvoker(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost:5034/api/auth/login");

        // Act
        var response = await invoker.SendAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(innerHandler.LastRequest);
        Assert.Null(innerHandler.LastRequest.Headers.Authorization);
    }

    [Fact]
    public async Task SendAsync_WhenAuthenticatedWithoutToken_DoesNotAttachAuthorizationHeader()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "testadmin")
        };
        var identity = new ClaimsIdentity(claims, "Cookies");
        httpContext.User = new ClaimsPrincipal(identity);

        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
        var innerHandler = new DummyInnerHandler();
        var handler = new BearerTokenHandler(httpContextAccessor)
        {
            InnerHandler = innerHandler
        };

        var invoker = new HttpMessageInvoker(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost:5034/api/auth/me");

        // Act
        var response = await invoker.SendAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(innerHandler.LastRequest);
        Assert.Null(innerHandler.LastRequest.Headers.Authorization);
    }
}

