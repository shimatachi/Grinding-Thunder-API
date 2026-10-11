using System.Net.Http.Json;
using System.Text.Json;
using GrindingThunder.Admin.Models;

namespace GrindingThunder.Admin.Services;

/// <summary>
/// Typed HTTP client that communicates with the backend Grinding Thunder REST API.
/// </summary>
public class ApiClient(HttpClient httpClient) : IApiClient
{
    private readonly HttpClient _httpClient = httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string BaseUrl => _httpClient.BaseAddress?.ToString().TrimEnd('/') ?? "http://localhost:5034";

    public async Task<LoginResult> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new LoginRequest(username, password);
            var response = await _httpClient.PostAsJsonAsync("api/auth/login", request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions, cancellationToken);
                if (payload != null)
                {
                    return new LoginResult(
                        Success: true,
                        Token: payload.Token,
                        ExpiresAt: payload.ExpiresAt,
                        Username: payload.Username,
                        Roles: payload.Roles);
                }
            }

            // Extract error message from API response
            string? errorMessage = null;
            try
            {
                var errorObj = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(JsonOptions, cancellationToken);
                if (!string.IsNullOrWhiteSpace(errorObj?.Message))
                {
                    errorMessage = errorObj.Message;
                }
            }
            catch
            {
                // Not a JSON error object, attempt reading as string
            }

            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                var raw = await response.Content.ReadAsStringAsync(cancellationToken);
                errorMessage = !string.IsNullOrWhiteSpace(raw) ? raw.Trim('"', ' ', '\r', '\n') : $"Authentication failed (status {(int)response.StatusCode}).";
            }

            return new LoginResult(Success: false, ErrorMessage: errorMessage);
        }
        catch (HttpRequestException ex)
        {
            return new LoginResult(Success: false, ErrorMessage: $"Unable to connect to API ({BaseUrl}): {ex.Message}");
        }
        catch (Exception ex)
        {
            return new LoginResult(Success: false, ErrorMessage: $"Authentication request error: {ex.Message}");
        }
    }

    public async Task<CurrentUserDto?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("api/auth/me", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<CurrentUserDto>(JsonOptions, cancellationToken);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<(bool IsConnected, CurrentUserDto? User, string? ErrorMessage)> VerifySessionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("api/auth/me", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var user = await response.Content.ReadFromJsonAsync<CurrentUserDto>(JsonOptions, cancellationToken);
                return (true, user, null);
            }

            return (false, null, $"API responded with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }
        catch (HttpRequestException ex)
        {
            return (false, null, $"Cannot connect to backend API at {BaseUrl}: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, null, $"API communication failure: {ex.Message}");
        }
    }
}

