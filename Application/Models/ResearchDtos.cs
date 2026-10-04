namespace GrindingThunder.Api.Application.Models;

public record ResearchCalculationRequest(
    Guid ResearchTreeVersionId,
    Guid TargetVehicleId,
    List<Guid> UnlockedVehicleIds,
    List<Guid> FillerTargetIds,
    int AverageRpPerMatch,
    int AverageNetSlPerMatch);

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
