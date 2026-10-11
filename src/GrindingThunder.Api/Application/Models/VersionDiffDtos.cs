using System.Text.Json.Serialization;
using GrindingThunder.Api.Domain.Entities;

namespace GrindingThunder.Api.Application.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RankChangeType
{
    Added = 0,
    Removed = 1,
    Modified = 2
}

public class VehicleDiffDto
{
    public Guid VehicleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Rank { get; set; }
    public int RpCost { get; set; }
    public int SlCost { get; set; }
    public VehicleAcquisitionType AcquisitionType { get; set; }
    public int TreeColumn { get; set; }
    public int TreeRow { get; set; }
    public Guid? FolderParentVehicleId { get; set; }
}

public class VehicleModifiedDiffDto
{
    public Guid VehicleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int OldRpCost { get; set; }
    public int NewRpCost { get; set; }
    public int RpDelta { get; set; }
    public int OldSlCost { get; set; }
    public int NewSlCost { get; set; }
    public int SlDelta { get; set; }
    public int OldRankNumber { get; set; }
    public int NewRankNumber { get; set; }
    public VehicleAcquisitionType OldAcquisitionType { get; set; }
    public VehicleAcquisitionType NewAcquisitionType { get; set; }
    public int OldColumn { get; set; }
    public int NewColumn { get; set; }
    public int OldRow { get; set; }
    public int NewRow { get; set; }
    public Guid? OldParentVehicleId { get; set; }
    public Guid? NewParentVehicleId { get; set; }
}

public class PrerequisiteDiffDto
{
    public Guid VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public Guid PrerequisiteVehicleId { get; set; }
    public string PrerequisiteVehicleName { get; set; } = string.Empty;
}

public class RankDiffDto
{
    public int RankNumber { get; set; }
    public int? OldRequiredVehiclesUnlocked { get; set; }
    public int? NewRequiredVehiclesUnlocked { get; set; }
    public RankChangeType ChangeType { get; set; }
}

public class VersionDiffSummaryDto
{
    public int AddedVehiclesCount { get; set; }
    public int RemovedVehiclesCount { get; set; }
    public int ModifiedVehiclesCount { get; set; }
    public int AddedPrerequisitesCount { get; set; }
    public int RemovedPrerequisitesCount { get; set; }
    public long NetRpDelta { get; set; }
    public long NetSlDelta { get; set; }
}

public class VersionDiffResult
{
    public Guid SourceVersionId { get; set; }
    public Guid TargetVersionId { get; set; }
    public string SourceGameUpdateVersion { get; set; } = string.Empty;
    public string TargetGameUpdateVersion { get; set; } = string.Empty;
    public List<VehicleDiffDto> AddedVehicles { get; set; } = new();
    public List<VehicleDiffDto> RemovedVehicles { get; set; } = new();
    public List<VehicleModifiedDiffDto> ModifiedVehicles { get; set; } = new();
    public List<PrerequisiteDiffDto> AddedPrerequisites { get; set; } = new();
    public List<PrerequisiteDiffDto> RemovedPrerequisites { get; set; } = new();
    public List<RankDiffDto> RankChanges { get; set; } = new();
    public VersionDiffSummaryDto Summary { get; set; } = new();
}

