using System.Net.Http.Json;
using System.Net;
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

    public Task<ApiResponse<IReadOnlyList<NationDto>>> GetNationsAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<NationDto>("api/admin/nations", cancellationToken);

    public Task<ApiResponse<NationDto>> GetNationAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetAsync<NationDto>($"api/admin/nations/{id}", cancellationToken);

    public Task<ApiResponse<NationDto>> CreateNationAsync(CreateNationRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<CreateNationRequest, NationDto>("api/admin/nations", request, cancellationToken);

    public Task<ApiResponse> UpdateNationAsync(Guid id, UpdateNationRequest request, CancellationToken cancellationToken = default) =>
        PutAsync($"api/admin/nations/{id}", request, cancellationToken);

    public Task<ApiResponse> DeleteNationAsync(Guid id, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/admin/nations/{id}", cancellationToken);

    public Task<ApiResponse<IReadOnlyList<VehicleTypeDto>>> GetVehicleTypesAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<VehicleTypeDto>("api/admin/vehicle-types", cancellationToken);

    public Task<ApiResponse<VehicleTypeDto>> GetVehicleTypeAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetAsync<VehicleTypeDto>($"api/admin/vehicle-types/{id}", cancellationToken);

    public Task<ApiResponse<VehicleTypeDto>> CreateVehicleTypeAsync(CreateVehicleTypeRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<CreateVehicleTypeRequest, VehicleTypeDto>("api/admin/vehicle-types", request, cancellationToken);

    public Task<ApiResponse> UpdateVehicleTypeAsync(Guid id, UpdateVehicleTypeRequest request, CancellationToken cancellationToken = default) =>
        PutAsync($"api/admin/vehicle-types/{id}", request, cancellationToken);

    public Task<ApiResponse> DeleteVehicleTypeAsync(Guid id, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/admin/vehicle-types/{id}", cancellationToken);

    public Task<ApiResponse<IReadOnlyList<VehicleDto>>> GetVehiclesAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<VehicleDto>("api/admin/vehicles", cancellationToken);

    public Task<ApiResponse<VehicleDto>> GetVehicleAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetAsync<VehicleDto>($"api/admin/vehicles/{id}", cancellationToken);

    public Task<ApiResponse<VehicleDto>> CreateVehicleAsync(CreateVehicleRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<CreateVehicleRequest, VehicleDto>("api/admin/vehicles", request, cancellationToken);

    public Task<ApiResponse> UpdateVehicleAsync(Guid id, UpdateVehicleRequest request, CancellationToken cancellationToken = default) =>
        PutAsync($"api/admin/vehicles/{id}", request, cancellationToken);

    public Task<ApiResponse> DeleteVehicleAsync(Guid id, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/admin/vehicles/{id}", cancellationToken);

    public Task<ApiResponse<IReadOnlyList<GameUpdateDto>>> GetGameUpdatesAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<GameUpdateDto>("api/admin/game-updates", cancellationToken);

    public Task<ApiResponse<GameUpdateDto>> GetGameUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetAsync<GameUpdateDto>($"api/admin/game-updates/{id}", cancellationToken);

    public Task<ApiResponse<GameUpdateDto>> CreateGameUpdateAsync(CreateGameUpdateRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<CreateGameUpdateRequest, GameUpdateDto>("api/admin/game-updates", request, cancellationToken);

    public Task<ApiResponse> UpdateGameUpdateAsync(Guid id, UpdateGameUpdateRequest request, CancellationToken cancellationToken = default) =>
        PutAsync($"api/admin/game-updates/{id}", request, cancellationToken);

    public Task<ApiResponse> DeleteGameUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/admin/game-updates/{id}", cancellationToken);

    public Task<ApiResponse<IReadOnlyList<ResearchTreeDto>>> GetResearchTreesAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<ResearchTreeDto>("api/admin/research-trees", cancellationToken);

    public Task<ApiResponse<ResearchTreeDto>> GetResearchTreeAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetAsync<ResearchTreeDto>($"api/admin/research-trees/{id}", cancellationToken);

    public Task<ApiResponse<ResearchTreeDto>> CreateResearchTreeAsync(CreateResearchTreeRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<CreateResearchTreeRequest, ResearchTreeDto>("api/admin/research-trees", request, cancellationToken);

    public Task<ApiResponse> DeleteResearchTreeAsync(Guid id, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/admin/research-trees/{id}", cancellationToken);

    public Task<ApiResponse<IReadOnlyList<NationVersionsDto>>> GetNationsWithVersionsAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<NationVersionsDto>("api/nations", cancellationToken);

    public Task<ApiResponse<ResearchTreeVersionSummaryDto>> GetResearchTreeVersionAsync(Guid versionId, CancellationToken cancellationToken = default) =>
        GetAsync<ResearchTreeVersionSummaryDto>($"api/admin/research-tree-versions/{versionId}", cancellationToken);

    public Task<ApiResponse<CreatedVersionDto>> CreateDraftVersionAsync(CreateDraftVersionRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<CreateDraftVersionRequest, CreatedVersionDto>("api/admin/research-tree-versions", request, cancellationToken);

    public Task<ApiResponse<CreatedVersionDto>> CloneResearchTreeVersionAsync(Guid versionId, CloneVersionRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<CloneVersionRequest, CreatedVersionDto>($"api/admin/research-tree-versions/{versionId}/clone", request, cancellationToken);

    public Task<ApiResponse> DeleteResearchTreeVersionAsync(Guid versionId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/admin/research-tree-versions/{versionId}", cancellationToken);

    public async Task<ApiResponse> PublishResearchTreeVersionAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsync($"api/admin/research-tree-versions/{versionId}/publish", null, cancellationToken);
            return response.IsSuccessStatusCode ? new ApiResponse(response.StatusCode) :
                new ApiResponse(response.StatusCode, await ReadErrorAsync(response, cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            return new ApiResponse(HttpStatusCode.ServiceUnavailable, ConnectionError(ex));
        }
    }

    public Task<ApiResponse<VersionValidationDto>> ValidateResearchTreeVersionAsync(Guid versionId, CancellationToken cancellationToken = default) =>
        GetAsync<VersionValidationDto>($"api/research-tree-versions/{versionId}/validate", cancellationToken);

    public Task<ApiResponse<VersionDiffDto>> CompareResearchTreeVersionsAsync(Guid sourceVersionId, Guid targetVersionId, CancellationToken cancellationToken = default) =>
        GetAsync<VersionDiffDto>($"api/research-tree-versions/diff?sourceVersionId={sourceVersionId}&targetVersionId={targetVersionId}", cancellationToken);

    public Task<ApiResponse<ImportPreviewDto>> PreviewImportAsync(Guid versionId, Stream file, string fileName, bool autoCreateMissingVehicles, CancellationToken cancellationToken = default) =>
        UploadAsync<ImportPreviewDto>($"api/admin/drafts/{versionId}/import/preview", file, fileName, autoCreateMissingVehicles, cancellationToken);

    public Task<ApiResponse<ImportCommitDto>> CommitImportAsync(Guid versionId, Stream file, string fileName, bool autoCreateMissingVehicles, CancellationToken cancellationToken = default) =>
        UploadAsync<ImportCommitDto>($"api/admin/drafts/{versionId}/import", file, fileName, autoCreateMissingVehicles, cancellationToken);

    private async Task<ApiResponse<T>> UploadAsync<T>(string uri, Stream file, string fileName, bool autoCreateMissingVehicles, CancellationToken cancellationToken)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StreamContent(file), "file", fileName);
            form.Add(new StringContent(autoCreateMissingVehicles.ToString().ToLowerInvariant()), "autoCreateMissingVehicles");
            var response = await _httpClient.PostAsync(uri, form, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new ApiResponse<T>(response.StatusCode,
                    await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken));
            }

            return new ApiResponse<T>(response.StatusCode, ErrorMessage: await ReadErrorAsync(response, cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            return new ApiResponse<T>(HttpStatusCode.ServiceUnavailable, ErrorMessage: ConnectionError(ex));
        }
    }

    public Task<ApiResponse<DraftVersionDetailDto>> GetDraftOverviewAsync(Guid versionId, CancellationToken cancellationToken = default) =>
        GetAsync<DraftVersionDetailDto>($"api/admin/drafts/{versionId}", cancellationToken);

    public Task<ApiResponse<TreeRankDto>> AddTreeRankAsync(Guid versionId, AddTreeRankRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<AddTreeRankRequest, TreeRankDto>($"api/admin/drafts/{versionId}/ranks", request, cancellationToken);

    public Task<ApiResponse> UpdateTreeRankAsync(Guid versionId, Guid rankId, UpdateTreeRankQuotaRequest request, CancellationToken cancellationToken = default) =>
        PutAsync($"api/admin/drafts/{versionId}/ranks/{rankId}", request, cancellationToken);

    public Task<ApiResponse> DeleteTreeRankAsync(Guid versionId, Guid rankId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/admin/drafts/{versionId}/ranks/{rankId}", cancellationToken);

    public Task<ApiResponse<VehicleTreeEntryDto>> AddVehicleTreeEntryAsync(Guid versionId, AddVehicleTreeEntryRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<AddVehicleTreeEntryRequest, VehicleTreeEntryDto>($"api/admin/drafts/{versionId}/entries", request, cancellationToken);

    public Task<ApiResponse> UpdateVehicleTreeEntryAsync(Guid versionId, Guid entryId, UpdateVehicleTreeEntryRequest request, CancellationToken cancellationToken = default) =>
        PutAsync($"api/admin/drafts/{versionId}/entries/{entryId}", request, cancellationToken);

    public Task<ApiResponse> DeleteVehicleTreeEntryAsync(Guid versionId, Guid entryId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/admin/drafts/{versionId}/entries/{entryId}", cancellationToken);

    public Task<ApiResponse<VehiclePrerequisiteDto>> AddVehiclePrerequisiteAsync(Guid versionId, AddPrerequisiteRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<AddPrerequisiteRequest, VehiclePrerequisiteDto>($"api/admin/drafts/{versionId}/prerequisites", request, cancellationToken);

    public Task<ApiResponse> DeleteVehiclePrerequisiteAsync(Guid versionId, Guid entryId, Guid prerequisiteEntryId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/admin/drafts/{versionId}/prerequisites/{entryId}/{prerequisiteEntryId}", cancellationToken);

    public Task<ApiResponse<IReadOnlyList<PublicVehicleTreeEntryDto>>> GetVehicleTreeAsync(Guid nationId, string? vehicleTypeName = null, CancellationToken cancellationToken = default)
    {
        var uri = $"api/vehicles/tree?nationId={nationId}";
        if (!string.IsNullOrWhiteSpace(vehicleTypeName))
        {
            uri += $"&type={Uri.EscapeDataString(vehicleTypeName)}";
        }

        return GetListAsync<PublicVehicleTreeEntryDto>(uri, cancellationToken);
    }

    private async Task<ApiResponse<IReadOnlyList<T>>> GetListAsync<T>(string uri, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(uri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var values = await response.Content.ReadFromJsonAsync<List<T>>(JsonOptions, cancellationToken) ?? [];
                return new ApiResponse<IReadOnlyList<T>>(response.StatusCode, values);
            }

            return new ApiResponse<IReadOnlyList<T>>(response.StatusCode, ErrorMessage: await ReadErrorAsync(response, cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            return new ApiResponse<IReadOnlyList<T>>(HttpStatusCode.ServiceUnavailable, ErrorMessage: ConnectionError(ex));
        }
    }

    private async Task<ApiResponse<T>> GetAsync<T>(string uri, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(uri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
                return new ApiResponse<T>(response.StatusCode, value);
            }

            return new ApiResponse<T>(response.StatusCode, ErrorMessage: await ReadErrorAsync(response, cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            return new ApiResponse<T>(HttpStatusCode.ServiceUnavailable, ErrorMessage: ConnectionError(ex));
        }
    }

    private async Task<ApiResponse<TResponse>> PostAsync<TRequest, TResponse>(string uri, TRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(uri, request, JsonOptions, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken);
                return new ApiResponse<TResponse>(response.StatusCode, value);
            }

            return new ApiResponse<TResponse>(response.StatusCode, ErrorMessage: await ReadErrorAsync(response, cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            return new ApiResponse<TResponse>(HttpStatusCode.ServiceUnavailable, ErrorMessage: ConnectionError(ex));
        }
    }

    private async Task<ApiResponse> PutAsync<TRequest>(string uri, TRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync(uri, request, JsonOptions, cancellationToken);
            return response.IsSuccessStatusCode
                ? new ApiResponse(response.StatusCode)
                : new ApiResponse(response.StatusCode, await ReadErrorAsync(response, cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            return new ApiResponse(HttpStatusCode.ServiceUnavailable, ConnectionError(ex));
        }
    }

    private async Task<ApiResponse> DeleteAsync(string uri, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.DeleteAsync(uri, cancellationToken);
            return response.IsSuccessStatusCode
                ? new ApiResponse(response.StatusCode)
                : new ApiResponse(response.StatusCode, await ReadErrorAsync(response, cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            return new ApiResponse(HttpStatusCode.ServiceUnavailable, ConnectionError(ex));
        }
    }

    private string ConnectionError(HttpRequestException ex) => $"Unable to connect to API ({BaseUrl}): {ex.Message}";

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return $"API request failed (status {(int)response.StatusCode}).";
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind == JsonValueKind.String)
            {
                return document.RootElement.GetString() ?? raw;
            }

            foreach (var propertyName in new[] { "message", "detail", "title" })
            {
                if (document.RootElement.TryGetProperty(propertyName, out var property) &&
                    property.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(property.GetString()))
                {
                    return property.GetString()!;
                }
            }

            if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                var messages = errors.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(item => !string.IsNullOrWhiteSpace(item));
                var joined = string.Join("; ", messages);
                if (joined.Length > 0) return joined;
            }
        }
        catch (JsonException)
        {
            // The API also returns plain-text error bodies for catalog endpoints.
        }

        return raw.Trim('"', ' ', '\r', '\n');
    }
}

