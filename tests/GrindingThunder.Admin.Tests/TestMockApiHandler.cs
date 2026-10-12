using System.Net;
using System.Text;
using System.Text.Json;

namespace GrindingThunder.Admin.Tests;

public class TestMockApiHandler : HttpMessageHandler
{
    public static readonly Guid NationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid VehicleTypeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid VehicleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid GameUpdateId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid ResearchTreeId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    public List<HttpRequestMessage> RecordedRequests { get; } = [];
    public bool ConflictNextCatalogMutation { get; set; }
    public bool ConflictNextDelete { get; set; }

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

        if (path.Contains("/api/admin/", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Headers.Authorization?.Scheme != "Bearer" || request.Headers.Authorization.Parameter != "test-jwt-token-xyz")
            {
                return JsonResponse(HttpStatusCode.Unauthorized, new { message = "Unauthorized" });
            }

            if (ConflictNextCatalogMutation && request.Method is { } method && (method == HttpMethod.Post || method == HttpMethod.Put))
            {
                ConflictNextCatalogMutation = false;
                return TextResponse(HttpStatusCode.Conflict, "A matching catalog item already exists.");
            }

            if (ConflictNextDelete && request.Method == HttpMethod.Delete)
            {
                ConflictNextDelete = false;
                return TextResponse(HttpStatusCode.Conflict, "Cannot delete this catalog item because it is in use.");
            }

            return HandleCatalogRequest(request, path);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage HandleCatalogRequest(HttpRequestMessage request, string path)
    {
        if (path.Equals("/api/admin/nations", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Method == HttpMethod.Get) return JsonResponse(HttpStatusCode.OK, new[] { new { id = NationId, name = "USA" } });
            if (request.Method == HttpMethod.Post) return JsonResponse(HttpStatusCode.Created, new { id = NationId, name = "USA" });
        }
        if (path.Equals($"/api/admin/nations/{NationId}", StringComparison.OrdinalIgnoreCase))
            return EntityResponse(request, new { id = NationId, name = "USA" });

        if (path.Equals("/api/admin/vehicle-types", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Method == HttpMethod.Get) return JsonResponse(HttpStatusCode.OK, new[] { new { id = VehicleTypeId, name = "Aviation" } });
            if (request.Method == HttpMethod.Post) return JsonResponse(HttpStatusCode.Created, new { id = VehicleTypeId, name = "Aviation" });
        }
        if (path.Equals($"/api/admin/vehicle-types/{VehicleTypeId}", StringComparison.OrdinalIgnoreCase))
            return EntityResponse(request, new { id = VehicleTypeId, name = "Aviation" });

        if (path.Equals("/api/admin/vehicles", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Method == HttpMethod.Get) return JsonResponse(HttpStatusCode.OK, new[] { new { id = VehicleId, name = "F-16A", imageUrl = "https://example.test/f16.png" } });
            if (request.Method == HttpMethod.Post) return JsonResponse(HttpStatusCode.Created, new { id = VehicleId, name = "F-16A", imageUrl = "https://example.test/f16.png" });
        }
        if (path.Equals($"/api/admin/vehicles/{VehicleId}", StringComparison.OrdinalIgnoreCase))
            return EntityResponse(request, new { id = VehicleId, name = "F-16A", imageUrl = "https://example.test/f16.png" });

        if (path.Equals("/api/admin/game-updates", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Method == HttpMethod.Get) return JsonResponse(HttpStatusCode.OK, new[] { new { id = GameUpdateId, version = "2.50", name = "Test Update", releaseDate = "2026-10-12" } });
            if (request.Method == HttpMethod.Post) return JsonResponse(HttpStatusCode.Created, new { id = GameUpdateId, version = "2.50", name = "Test Update", releaseDate = "2026-10-12" });
        }
        if (path.Equals($"/api/admin/game-updates/{GameUpdateId}", StringComparison.OrdinalIgnoreCase))
            return EntityResponse(request, new { id = GameUpdateId, version = "2.50", name = "Test Update", releaseDate = "2026-10-12" });

        if (path.Equals("/api/admin/research-trees", StringComparison.OrdinalIgnoreCase))
        {
            var tree = new { id = ResearchTreeId, nationId = NationId, nationName = "USA", vehicleTypeId = VehicleTypeId, vehicleTypeName = "Aviation" };
            if (request.Method == HttpMethod.Get) return JsonResponse(HttpStatusCode.OK, new[] { tree });
            if (request.Method == HttpMethod.Post) return JsonResponse(HttpStatusCode.Created, tree);
        }
        if (path.Equals($"/api/admin/research-trees/{ResearchTreeId}", StringComparison.OrdinalIgnoreCase))
            return EntityResponse(request, new { id = ResearchTreeId, nationId = NationId, nationName = "USA", vehicleTypeId = VehicleTypeId, vehicleTypeName = "Aviation" });

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage EntityResponse(HttpRequestMessage request, object entity)
    {
        if (request.Method == HttpMethod.Get) return JsonResponse(HttpStatusCode.OK, entity);
        if (request.Method == HttpMethod.Put || request.Method == HttpMethod.Delete) return new HttpResponseMessage(HttpStatusCode.NoContent);
        return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object body) => new(statusCode)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage TextResponse(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/plain")
    };
}

