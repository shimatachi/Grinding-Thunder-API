using System.Text.Json.Serialization;

namespace GrindingThunder.Admin.Models;

public enum ResearchOptimizationMode
{
    LeastRp = 0,
    LeastSl = 1,
    FewestVehicles = 2,
    FewestMatches = 3
}

public record PlayerRegisterRequest(string Username, string Email, string Password);

public record PlayerLoginRequest(string UsernameOrEmail, string Password);

public record PlayerTokenResponse(
    Guid PlayerAccountId,
    string Username,
    string Email,
    string Token,
    string TokenType,
    DateTime ExpiresAt);

public record PlayerProfileResponse(
    Guid PlayerAccountId,
    string Username,
    string Email);

public record PlayerCalculatorPreferences
{
    [JsonConverter(typeof(JsonStringEnumConverter<ResearchOptimizationMode>))]
    public ResearchOptimizationMode OptimizationMode { get; init; } = ResearchOptimizationMode.LeastRp;
    public int? AverageRpPerMatch { get; init; }
    public int? AverageNetSlPerMatch { get; init; }

    public PlayerCalculatorPreferences() { }
    public PlayerCalculatorPreferences(
        ResearchOptimizationMode optimizationMode,
        int? averageRpPerMatch = null,
        int? averageNetSlPerMatch = null)
    {
        OptimizationMode = optimizationMode;
        AverageRpPerMatch = averageRpPerMatch;
        AverageNetSlPerMatch = averageNetSlPerMatch;
    }
}

public record UpdatePlayerProgressRequest(
    List<Guid> UnlockedVehicleIds,
    PlayerCalculatorPreferences Preferences);

public record PlayerProgressResponse(
    List<Guid> UnlockedVehicleIds,
    PlayerCalculatorPreferences Preferences);

public record ResearchCalculationRequest(
    Guid ResearchTreeVersionId,
    Guid TargetVehicleId,
    List<Guid> UnlockedVehicleIds,
    List<Guid> FillerTargetIds,
    int AverageRpPerMatch,
    int AverageNetSlPerMatch,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ResearchOptimizationMode>))]
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

