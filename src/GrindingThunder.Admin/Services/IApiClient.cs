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

    /// <summary>
    /// Retrieves nations with their research trees and draft/published version
    /// snapshots from the public GET /api/nations endpoint.
    /// </summary>
    Task<ApiResponse<IReadOnlyList<NationVersionsDto>>> GetNationsWithVersionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves version metadata (status, game update, counts) from
    /// GET /api/admin/research-tree-versions/{versionId}. Works for drafts and published versions.
    /// </summary>
    Task<ApiResponse<ResearchTreeVersionSummaryDto>> GetResearchTreeVersionAsync(Guid versionId, CancellationToken cancellationToken = default);

    Task<ApiResponse<CreatedVersionDto>> CreateDraftVersionAsync(CreateDraftVersionRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CreatedVersionDto>> CloneResearchTreeVersionAsync(Guid versionId, CloneVersionRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse> DeleteResearchTreeVersionAsync(Guid versionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the full draft overview (ranks, entries, prerequisites) from
    /// GET /api/admin/drafts/{versionId}. Published versions respond with 409 Conflict.
    /// </summary>
    Task<ApiResponse<DraftVersionDetailDto>> GetDraftOverviewAsync(Guid versionId, CancellationToken cancellationToken = default);

    Task<ApiResponse<TreeRankDto>> AddTreeRankAsync(Guid versionId, AddTreeRankRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse> UpdateTreeRankAsync(Guid versionId, Guid rankId, UpdateTreeRankQuotaRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse> DeleteTreeRankAsync(Guid versionId, Guid rankId, CancellationToken cancellationToken = default);

    Task<ApiResponse<VehicleTreeEntryDto>> AddVehicleTreeEntryAsync(Guid versionId, AddVehicleTreeEntryRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse> UpdateVehicleTreeEntryAsync(Guid versionId, Guid entryId, UpdateVehicleTreeEntryRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse> DeleteVehicleTreeEntryAsync(Guid versionId, Guid entryId, CancellationToken cancellationToken = default);

    Task<ApiResponse<VehiclePrerequisiteDto>> AddVehiclePrerequisiteAsync(Guid versionId, AddPrerequisiteRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse> DeleteVehiclePrerequisiteAsync(Guid versionId, Guid entryId, Guid prerequisiteEntryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves versioned entries for a nation from the public GET /api/vehicles/tree
    /// endpoint, optionally filtered by vehicle type name. Used for read-only
    /// published version views.
    /// </summary>
    Task<ApiResponse<IReadOnlyList<PublicVehicleTreeEntryDto>>> GetVehicleTreeAsync(Guid nationId, string? vehicleTypeName = null, CancellationToken cancellationToken = default);
}

