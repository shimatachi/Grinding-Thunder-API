using GrindingThunder.Admin.Models;

namespace GrindingThunder.Admin.Services;

/// <summary>
/// Typed client interface for communicating with the Grinding Thunder REST API.
/// </summary>
public interface IApiClient
{
    /// <summary>
    /// Gets the configured base URL of the backend API.
    /// </summary>
    string BaseUrl { get; }

    /// <summary>
    /// Authenticates administrative credentials with the backend REST API.
    /// </summary>
    Task<LoginResult> LoginAsync(string username, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves current user details from GET /api/auth/me using the attached Bearer token.
    /// </summary>
    Task<CurrentUserDto?> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies live connectivity and authentication against GET /api/auth/me.
    /// </summary>
    Task<(bool IsConnected, CurrentUserDto? User, string? ErrorMessage)> VerifySessionAsync(CancellationToken cancellationToken = default);

    Task<ApiResponse<IReadOnlyList<NationDto>>> GetNationsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<NationDto>> GetNationAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ApiResponse<NationDto>> CreateNationAsync(CreateNationRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse> UpdateNationAsync(Guid id, UpdateNationRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse> DeleteNationAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ApiResponse<IReadOnlyList<VehicleTypeDto>>> GetVehicleTypesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<VehicleTypeDto>> GetVehicleTypeAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ApiResponse<VehicleTypeDto>> CreateVehicleTypeAsync(CreateVehicleTypeRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse> UpdateVehicleTypeAsync(Guid id, UpdateVehicleTypeRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse> DeleteVehicleTypeAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ApiResponse<IReadOnlyList<VehicleDto>>> GetVehiclesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<VehicleDto>> GetVehicleAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ApiResponse<VehicleDto>> CreateVehicleAsync(CreateVehicleRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse> UpdateVehicleAsync(Guid id, UpdateVehicleRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse> DeleteVehicleAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ApiResponse<IReadOnlyList<GameUpdateDto>>> GetGameUpdatesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<GameUpdateDto>> GetGameUpdateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ApiResponse<GameUpdateDto>> CreateGameUpdateAsync(CreateGameUpdateRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse> UpdateGameUpdateAsync(Guid id, UpdateGameUpdateRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse> DeleteGameUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ApiResponse<IReadOnlyList<ResearchTreeDto>>> GetResearchTreesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<ResearchTreeDto>> GetResearchTreeAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ApiResponse<ResearchTreeDto>> CreateResearchTreeAsync(CreateResearchTreeRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse> DeleteResearchTreeAsync(Guid id, CancellationToken cancellationToken = default);
}

