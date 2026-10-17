using GrindingThunder.Api.Domain.Entities;

namespace GrindingThunder.Api.Application.Models;

/// <summary>
/// Canonical strongly typed response model for the public vehicle tree endpoint (GET /api/vehicles/tree).
/// Represents one versioned vehicle entry within an immutable published tech tree snapshot.
/// </summary>
public record VehicleTreeResponseDto(
    Guid Id,
    Guid ResearchTreeId,
    Guid NationId,
    string NationName,
    Guid VehicleTypeId,
    string VehicleTypeName,
    Guid TreeRankId,
    Guid ResearchTreeVersionId,
    string Name,
    string? ImageUrl,
    string? WikiUrl,
    int RpCost,
    int SlCost,
    VehicleAcquisitionType AcquisitionType,
    bool IsFolderParent,
    Guid? FolderParentId,
    int TreeColumn,
    int TreeRow,
    List<Guid> PrerequisiteIds);

