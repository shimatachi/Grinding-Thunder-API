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
        var httpContext = _httpContextAccessor.HttpContext;
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
