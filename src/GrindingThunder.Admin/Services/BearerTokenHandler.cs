using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;

namespace GrindingThunder.Admin.Services;

/// <summary>
/// DelegatingHandler that extracts the stored JWT Bearer token from the current
/// HttpContext session and attaches it to outgoing HTTP requests.
/// </summary>
public class BearerTokenHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization != null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var httpContext = _httpContextAccessor.HttpContext;
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path.StartsWith("/api/player", StringComparison.OrdinalIgnoreCase))
        {
            if (httpContext != null &&
                !path.Equals("/api/player/auth/login", StringComparison.OrdinalIgnoreCase) &&
                !path.Equals("/api/player/auth/register", StringComparison.OrdinalIgnoreCase))
            {
                string? playerToken = null;
                try
                {
                    var playerAuth = await httpContext.AuthenticateAsync("PlayerAuth");
                    if (playerAuth.Succeeded && playerAuth.Principal != null)
                    {
                        playerToken = playerAuth.Properties?.GetTokenValue("access_token")
                            ?? playerAuth.Principal.FindFirst("access_token")?.Value
                            ?? playerAuth.Principal.FindFirst("player_token")?.Value;
                    }
                }
                catch
                {
                    // Fall back if authentication scheme is not registered in isolated unit tests
                }

                if (!string.IsNullOrWhiteSpace(playerToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", playerToken);
                }
            }

            return await base.SendAsync(request, cancellationToken);
        }

        if (httpContext?.User.Identity?.IsAuthenticated == true)
        {
            string? token = null;

            if (httpContext.RequestServices != null)
            {
                try
                {
                    token = await httpContext.GetTokenAsync("access_token");
                }
                catch
                {
                    // Fall back to claim if authentication service is not registered
                }
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                token = httpContext.User.FindFirst("access_token")?.Value;
            }

            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
