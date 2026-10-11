using GrindingThunder.Api.Domain.Entities;

namespace GrindingThunder.Api.Application.Models;

/// <summary>
/// Preview result of a tech tree tabular data file (CSV/XLSX) before committing changes to a draft version.
/// </summary>
public record TechTreeImportPreviewDto(
    bool IsValid,
    int TotalRowsDetected,
    int NewEntriesCount,
    int UpdatedEntriesCount,
    int AutoCreatedVehiclesCount,
    int CreatedRanksCount,
    List<ImportEntryPreviewDto> Entries,
    List<string> Errors,
    List<string> Warnings);

/// <summary>
/// Itemized preview for each vehicle tree entry parsed from the import file.
/// </summary>
public record ImportEntryPreviewDto(
    int RowNumber,
    string VehicleName,
    int RankNumber,
    int RpCost,
    int SlCost,
    int TreeColumn,
    int TreeRow,
    VehicleAcquisitionType AcquisitionType,
    string? FolderParentVehicleName,
    List<string> PrerequisiteVehicleNames,
    string Action, // "Create" or "Update"
    bool VehicleExistsInCatalog);

/// <summary>
/// Result returned after executing an atomic import commit into a target draft research tree version.
/// </summary>
public record TechTreeImportCommitResultDto(
    bool Success,
    int ImportedEntriesCount,
    int NewEntriesCount,
    int UpdatedEntriesCount,
    int AutoCreatedVehiclesCount,
    int CreatedRanksCount,
    List<string> Messages,
    List<string>? Errors = null);

