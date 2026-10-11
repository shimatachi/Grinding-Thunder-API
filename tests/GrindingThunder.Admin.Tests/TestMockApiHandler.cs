using System.Net;
using System.Text;
using System.Text.Json;

namespace GrindingThunder.Admin.Tests;

public class TestMockApiHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> RecordedRequests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RecordedRequests.Add(request);

        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (request.Method == HttpMethod.Post && path.EndsWith("/api/auth/login", StringComparison.OrdinalIgnoreCase))
        {
            var content = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : string.Empty;
            using var doc = JsonDocument.Parse(content);
            var username = doc.RootElement.GetProperty("username").GetString();
            var password = doc.RootElement.GetProperty("password").GetString();

            if (username == "admin" && password == "admin123")
            {
                var responseBody = JsonSerializer.Serialize(new
                {
                    token = "test-jwt-token-xyz",
                    tokenType = "Bearer",
                    expiresAt = DateTime.UtcNow.AddHours(2),
                    username = "admin",
                    roles = new[] { "Admin", "Publisher", "Editor" }
                });

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
                };
            }

            var errorBody = JsonSerializer.Serialize(new
            {
                message = "Invalid username or password."
            });

            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(errorBody, Encoding.UTF8, "application/json")
            };
        }

        if (request.Method == HttpMethod.Get && path.EndsWith("/api/auth/me", StringComparison.OrdinalIgnoreCase))
        {
            var authHeader = request.Headers.Authorization;
            if (authHeader?.Scheme == "Bearer" && authHeader.Parameter == "test-jwt-token-xyz")
            {
                var responseBody = JsonSerializer.Serialize(new
                {
                    username = "admin",
                    roles = new[] { "Admin", "Publisher", "Editor" }
                });

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
                };
            }

            var unauthorizedBody = JsonSerializer.Serialize(new
            {
                message = "Unauthorized"
            });

            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(unauthorizedBody, Encoding.UTF8, "application/json")
            };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}

