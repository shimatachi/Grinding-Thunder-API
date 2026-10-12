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
public record TreeVersionListItemDto(Guid Id, int Status, string UpdateVersion, List<TreeRankSummaryDto> TreeRanks);
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
    List<Guid> PrerequisiteIds);

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

    public int RpCost { get; set; }
    public int SlCost { get; set; }
    public int TreeColumn { get; set; }
    public int TreeRow { get; set; }

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
    public int RpCost { get; set; }

    [Display(Name = "SL cost")]
    public int SlCost { get; set; }

    [Display(Name = "Tree column")]
    public int TreeColumn { get; set; }

    [Display(Name = "Tree row")]
    public int TreeRow { get; set; }

    [Display(Name = "Acquisition type")]
    public VehicleAcquisitionType AcquisitionType { get; set; } = VehicleAcquisitionType.Researchable;

    [Display(Name = "Folder parent")]
    public Guid? FolderParentEntryId { get; set; }
}