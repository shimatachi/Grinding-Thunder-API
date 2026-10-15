using GrindingThunder.Api.Domain.Entities;

namespace GrindingThunder.Api.Application.Models;

/// <summary>
/// Request to clone an existing research tree version into an editable draft for a target game update.
/// </summary>
public record CloneVersionRequest(Guid TargetGameUpdateId);

/// <summary>
/// Request to create an initial blank draft research tree version for a research tree and game update.
/// </summary>
public record CreateDraftVersionRequest(Guid ResearchTreeId, Guid GameUpdateId);

/// <summary>
/// Response returned upon successfully cloning or creating a research tree version.
/// </summary>
public record ClonedVersionResponse(
    Guid Id,
    Guid ResearchTreeId,
    Guid GameUpdateId,
    string Status);

// Catalog DTOs - ResearchTree
public record CreateResearchTreeRequest(Guid NationId, Guid VehicleTypeId);
public record ResearchTreeDto(Guid Id, Guid NationId, string NationName, Guid VehicleTypeId, string VehicleTypeName);

// Catalog DTOs - Nation
public record CreateNationRequest(string Name);
public record UpdateNationRequest(string Name);
public record NationDto(Guid Id, string Name);

// Catalog DTOs - VehicleType
public record CreateVehicleTypeRequest(string Name);
public record UpdateVehicleTypeRequest(string Name);
public record VehicleTypeDto(Guid Id, string Name);

// Catalog DTOs - Vehicle
public record CreateVehicleRequest(Guid ResearchTreeId, string Name, string? ImageUrl = null);
public record UpdateVehicleRequest(string Name, string? ImageUrl = null);
public record VehicleDto(Guid Id, string Name, string? ImageUrl,
    Guid ResearchTreeId, Guid NationId, string NationName, Guid VehicleTypeId, string VehicleTypeName);

// Catalog DTOs - GameUpdate
public record CreateGameUpdateRequest(string Version, string? Name = null, DateOnly? ReleaseDate = null);
public record UpdateGameUpdateRequest(string Version, string? Name = null, DateOnly? ReleaseDate = null);
public record GameUpdateDto(Guid Id, string Version, string? Name, DateOnly? ReleaseDate);

// Draft Editing DTOs - TreeRank
public record AddTreeRankRequest(int RankNumber, int RequiredVehiclesUnlocked);
public record UpdateTreeRankQuotaRequest(int RequiredVehiclesUnlocked);
public record TreeRankDto(Guid Id, Guid ResearchTreeVersionId, int RankNumber, int RequiredVehiclesUnlocked);

// Draft Editing DTOs - VehicleTreeEntry
public record AddVehicleTreeEntryRequest(
    Guid VehicleId,
    Guid TreeRankId,
    int RpCost,
    int SlCost,
    int TreeColumn,
    int TreeRow,
    VehicleAcquisitionType AcquisitionType = VehicleAcquisitionType.Researchable,
    Guid? FolderParentEntryId = null);

public record UpdateVehicleTreeEntryRequest(
    int RpCost,
    int SlCost,
    int TreeColumn,
    int TreeRow,
    Guid TreeRankId,
    VehicleAcquisitionType AcquisitionType = VehicleAcquisitionType.Researchable,
    Guid? FolderParentEntryId = null);

public record UpdateEntryCostsRequest(int RpCost, int SlCost);
public record UpdateEntryCoordinatesRequest(int TreeColumn, int TreeRow);
public record UpdateEntryFolderParentRequest(Guid? FolderParentEntryId);

public record VehicleTreeEntryDto(
    Guid Id,
    Guid ResearchTreeVersionId,
    Guid VehicleId,
    Guid TreeRankId,
    int RpCost,
    int SlCost,
    int TreeColumn,
    int TreeRow,
    VehicleAcquisitionType AcquisitionType,
    Guid? FolderParentEntryId,
    string? VehicleName = null);

// Draft Editing DTOs - VehiclePrerequisite
public record AddPrerequisiteRequest(
    Guid VehicleTreeEntryId,
    Guid PrerequisiteVehicleTreeEntryId);

public record VehiclePrerequisiteDto(
    Guid VehicleTreeEntryId,
    Guid PrerequisiteVehicleTreeEntryId,
    Guid ResearchTreeVersionId);

// Draft Overview DTO
public record DraftVersionDetailDto(
    Guid Id,
    Guid ResearchTreeId,
    Guid GameUpdateId,
    string Status,
    List<TreeRankDto> TreeRanks,
    List<VehicleTreeEntryDto> Entries,
    List<VehiclePrerequisiteDto> Prerequisites);

