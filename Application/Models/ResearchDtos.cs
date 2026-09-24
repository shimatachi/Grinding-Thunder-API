namespace GrindingThunder.Api.Application.Models;

public record ResearchCalculationRequest(
    Guid TargetVehicleId,
    List<Guid> UnlockedVehicleIds,
    List<Guid> FillerTargetIds,
    int AverageRpPerMatch);

public record VehicleRpSummary(
    Guid VehicleId,
    string Name,
    int RpRemaining,
    bool IsRankGateFiller);

public record RankDeficitDto(
    int RankNumber,
    int Shortfall,
    int Required);

public record ResearchCalculationResult(
    Guid TargetVehicleId,
    int TotalRpRequired,
    int EstimatedMatches,
    List<VehicleRpSummary> RequiredVehicles,
    List<RankDeficitDto> RankDeficits);
