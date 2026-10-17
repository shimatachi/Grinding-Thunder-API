using System.ComponentModel.DataAnnotations;

namespace GrindingThunder.Admin.Models;

/// <summary>
/// Mirrors the API's VehicleAcquisitionType enum. The API serializes enum values
/// as numbers, so the numeric values must stay aligned with the backend.
/// </summary>
public enum VehicleAcquisitionType
{
    Researchable = 0,
    Premium = 1,
    Squadron = 2,
    Event = 3,
    Pack = 4,
    Hidden = 5
}

// Research tree version lifecycle DTOs (AdminResearchTreeVersionsController).
public record CreateDraftVersionRequest(Guid ResearchTreeId, Guid GameUpdateId);
public record CloneVersionRequest(Guid TargetGameUpdateId);
public record CreatedVersionDto(Guid Id, Guid ResearchTreeId, Guid GameUpdateId, string Status);

public record VersionValidationDto(bool IsValid, List<string> Errors, List<string> Warnings);
public record ImportPreviewDto(bool IsValid, int TotalRowsDetected, int NewEntriesCount, int UpdatedEntriesCount,
    int AutoCreatedVehiclesCount, int CreatedRanksCount, List<ImportEntryPreviewDto> Entries,
    List<string> Errors, List<string> Warnings);
public record ImportEntryPreviewDto(int RowNumber, string VehicleName, int RankNumber, int RpCost, int SlCost,
    int TreeColumn, int TreeRow, VehicleAcquisitionType AcquisitionType, string? FolderParentVehicleName,
    List<string> PrerequisiteVehicleNames, string Action, bool VehicleExistsInCatalog);
public record ImportCommitDto(bool Success, int ImportedEntriesCount, int NewEntriesCount, int UpdatedEntriesCount,
    int AutoCreatedVehiclesCount, int CreatedRanksCount, List<string> Messages, List<string>? Errors);
public record VersionDiffSummaryDto(int AddedVehiclesCount, int RemovedVehiclesCount, int ModifiedVehiclesCount,
    int AddedPrerequisitesCount, int RemovedPrerequisitesCount, long NetRpDelta, long NetSlDelta);
public record VehicleDiffDto(Guid VehicleId, string Name, int Rank, int RpCost, int SlCost);
public record VehicleModifiedDiffDto(Guid VehicleId, string Name, int OldRpCost, int NewRpCost, int RpDelta,
    int OldSlCost, int NewSlCost, int SlDelta, int OldRankNumber, int NewRankNumber,
    VehicleAcquisitionType OldAcquisitionType, VehicleAcquisitionType NewAcquisitionType,
    int OldColumn, int NewColumn, int OldRow, int NewRow, Guid? OldParentVehicleId, Guid? NewParentVehicleId);
public record PrerequisiteDiffDto(Guid VehicleId, string VehicleName, Guid PrerequisiteVehicleId, string PrerequisiteVehicleName);
public record RankDiffDto(int RankNumber, int? OldRequiredVehiclesUnlocked, int? NewRequiredVehiclesUnlocked, string ChangeType);
public record VersionDiffDto(Guid SourceVersionId, Guid TargetVersionId, string SourceGameUpdateVersion,
    string TargetGameUpdateVersion, List<VehicleDiffDto> AddedVehicles, List<VehicleDiffDto> RemovedVehicles,
    List<VehicleModifiedDiffDto> ModifiedVehicles, List<PrerequisiteDiffDto> AddedPrerequisites,
    List<PrerequisiteDiffDto> RemovedPrerequisites, List<RankDiffDto> RankChanges, VersionDiffSummaryDto Summary);

public record ResearchTreeVersionSummaryDto(
    Guid Id,
    Guid ResearchTreeId,
    Guid GameUpdateId,
    string GameUpdateVersion,
    string Status,
    int RankCount,
    int EntryCount);

// Nested projection of the public GET /api/nations payload used to list
// the draft and published version snapshots of each research tree.
public record NationVersionsDto(Guid Id, string Name, List<TreeWithVersionsDto> ResearchTrees);
public record TreeWithVersionsDto(Guid Id, Guid VehicleTypeId, string VehicleTypeName, List<TreeVersionListItemDto> Versions);
public record TreeVersionListItemDto(Guid Id, Guid GameUpdateId, int Status, string UpdateVersion, List<TreeRankSummaryDto> TreeRanks);
public record TreeRankSummaryDto(Guid Id, int RankNumber, int RequiredVehiclesUnlocked);

// Draft editing DTOs (AdminDraftsController).
public record TreeRankDto(Guid Id, Guid ResearchTreeVersionId, int RankNumber, int RequiredVehiclesUnlocked);
public record AddTreeRankRequest(int RankNumber, int RequiredVehiclesUnlocked);
public record UpdateTreeRankQuotaRequest(int RequiredVehiclesUnlocked);

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

public record UpdateEntryCoordinatesRequest(int TreeColumn, int TreeRow);

public record VehiclePrerequisiteDto(
    Guid VehicleTreeEntryId,
    Guid PrerequisiteVehicleTreeEntryId,
    Guid ResearchTreeVersionId);

public record AddPrerequisiteRequest(
    Guid VehicleTreeEntryId,
    Guid PrerequisiteVehicleTreeEntryId);

public record DraftVersionDetailDto(
    Guid Id,
    Guid ResearchTreeId,
    Guid GameUpdateId,
    string Status,
    List<TreeRankDto> TreeRanks,
    List<VehicleTreeEntryDto> Entries,
    List<VehiclePrerequisiteDto> Prerequisites);

// Read-only entry projection from the public GET /api/vehicles/tree endpoint.
// Id and FolderParentId are stable Vehicle IDs (not versioned entry IDs) and
// PrerequisiteIds reference prerequisite vehicles.
public record PublicVehicleTreeEntryDto(
    Guid Id,
    Guid TreeRankId,
    Guid ResearchTreeVersionId,
    string Name,
    string? ImageUrl,
    int RpCost,
    int SlCost,
    VehicleAcquisitionType AcquisitionType,
    bool IsFolderParent,
    Guid? FolderParentId,
    int TreeColumn,
    int TreeRow,
    List<Guid> PrerequisiteIds,
    string? WikiUrl = null);

// Form input models for the version editor pages.
public class AddRankInputModel
{
    public int RankNumber { get; set; }
    public int RequiredVehiclesUnlocked { get; set; }
}

public class AddEntryInputModel
{
    [Required(ErrorMessage = "Vehicle is required.")]
    public Guid VehicleId { get; set; }

    [Required(ErrorMessage = "Rank is required.")]
    public Guid TreeRankId { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "RP cost cannot be negative.")]
    public int RpCost { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "SL cost cannot be negative.")]
    public int SlCost { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Tree column must be at least 1.")]
    public int TreeColumn { get; set; } = 1;

    [Range(1, int.MaxValue, ErrorMessage = "Tree row must be at least 1.")]
    public int TreeRow { get; set; } = 1;

    public VehicleAcquisitionType AcquisitionType { get; set; } = VehicleAcquisitionType.Researchable;

    public Guid? FolderParentEntryId { get; set; }
}

public class AddPrerequisiteInputModel
{
    [Required(ErrorMessage = "Vehicle entry is required.")]
    public Guid VehicleTreeEntryId { get; set; }

    [Required(ErrorMessage = "Prerequisite entry is required.")]
    public Guid PrerequisiteVehicleTreeEntryId { get; set; }
}

public class EntryInputModel
{
    [Required(ErrorMessage = "Rank is required.")]
    [Display(Name = "Rank")]
    public Guid TreeRankId { get; set; }

    [Display(Name = "RP cost")]
    [Range(0, int.MaxValue, ErrorMessage = "RP cost cannot be negative.")]
    public int RpCost { get; set; }

    [Display(Name = "SL cost")]
    [Range(0, int.MaxValue, ErrorMessage = "SL cost cannot be negative.")]
    public int SlCost { get; set; }

    [Display(Name = "Tree column")]
    [Range(1, int.MaxValue, ErrorMessage = "Tree column must be at least 1.")]
    public int TreeColumn { get; set; } = 1;

    [Display(Name = "Tree row")]
    [Range(1, int.MaxValue, ErrorMessage = "Tree row must be at least 1.")]
    public int TreeRow { get; set; } = 1;

    [Display(Name = "Acquisition type")]
    public VehicleAcquisitionType AcquisitionType { get; set; } = VehicleAcquisitionType.Researchable;

    [Display(Name = "Folder parent")]
    public Guid? FolderParentEntryId { get; set; }
}

public class VisualEntryInputModel : EntryInputModel
{
    public Guid EntryId { get; set; }
}

public class LayoutPositionInputModel
{
    public Guid EntryId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Tree column must be at least 1.")]
    public int TreeColumn { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Tree row must be at least 1.")]
    public int TreeRow { get; set; }
}

public class CreateVehicleEntryInputModel : EntryInputModel
{
    public string Name { get; set; } = string.Empty;

    [Url(ErrorMessage = "Image URL must be a valid absolute URL.")]
    [Display(Name = "Image URL")]
    public string? ImageUrl { get; set; }

    [Url(ErrorMessage = "Wiki URL must be a valid absolute URL.")]
    [StringLength(2048, ErrorMessage = "Wiki URL cannot exceed 2048 characters.")]
    [Display(Name = "Wiki URL")]
    public string? WikiUrl { get; set; }
}
