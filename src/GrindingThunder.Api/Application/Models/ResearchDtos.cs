using System.Text.Json.Serialization;

namespace GrindingThunder.Api.Application.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResearchOptimizationMode
{
    LeastRp = 0,
    LeastSl = 1,
    FewestVehicles = 2,
    FewestMatches = 3
}

public record ResearchCalculationRequest(
    Guid ResearchTreeVersionId,
    Guid TargetVehicleId,
    List<Guid> UnlockedVehicleIds,
    List<Guid> FillerTargetIds,
    int AverageRpPerMatch,
    int AverageNetSlPerMatch,
    ResearchOptimizationMode OptimizationMode = ResearchOptimizationMode.LeastRp);

public record VehicleRpSummary(
    Guid VehicleId,
    string Name,
    int RpRemaining,
    int SlRemaining,
    bool IsRankGateFiller);

public record RankDeficitDto(
    int RankNumber,
    int Shortfall,
    int Required);

public record ResearchCalculationResult(
    Guid TargetVehicleId,
    long TotalRpRequired,
    long TotalSlRequired,
    long RpEstimatedMatches,
    long SlEstimatedMatches,
    long EstimatedMatches,
    List<VehicleRpSummary> RequiredVehicles,
    List<RankDeficitDto> RankDeficits);
