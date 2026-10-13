using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using GrindingThunder.Api.Domain.Entities;

namespace GrindingThunder.Api.Application.Models;

public class PlayerRegisterRequest
{
    [Required, StringLength(64), RegularExpression(@"[a-zA-Z0-9_.-]+")]
    public string Username { get; init; } = string.Empty;
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = string.Empty;
    [Required, StringLength(256, MinimumLength = 8)]
    public string Password { get; init; } = string.Empty;

    public PlayerRegisterRequest() { }
    public PlayerRegisterRequest(string username, string email, string password)
        => (Username, Email, Password) = (username, email, password);
}

public class PlayerLoginRequest
{
    [Required, StringLength(254)]
    public string UsernameOrEmail { get; init; } = string.Empty;
    [Required, StringLength(256)]
    public string Password { get; init; } = string.Empty;

    public PlayerLoginRequest() { }
    public PlayerLoginRequest(string usernameOrEmail, string password)
        => (UsernameOrEmail, Password) = (usernameOrEmail, password);
}

public record PlayerTokenResponse(
    Guid PlayerAccountId, string Username, string Email, string Token, string TokenType, DateTime ExpiresAt);

public record PlayerProfileResponse(Guid PlayerAccountId, string Username, string Email);

/// <summary>Null match averages mean the player has not supplied an estimate yet.</summary>
public record PlayerCalculatorPreferences
{
    [EnumDataType(typeof(ResearchOptimizationMode)), JsonConverter(typeof(JsonStringEnumConverter<ResearchOptimizationMode>))]
    public ResearchOptimizationMode OptimizationMode { get; init; } = ResearchOptimizationMode.LeastRp;
    [Range(1, int.MaxValue)]
    public int? AverageRpPerMatch { get; init; }
    [Range(1, int.MaxValue)]
    public int? AverageNetSlPerMatch { get; init; }

    public PlayerCalculatorPreferences() { }
    public PlayerCalculatorPreferences(ResearchOptimizationMode optimizationMode, int? averageRpPerMatch = null, int? averageNetSlPerMatch = null)
        => (OptimizationMode, AverageRpPerMatch, AverageNetSlPerMatch) = (optimizationMode, averageRpPerMatch, averageNetSlPerMatch);
}

/// <summary>PUT replaces the entire set and preferences. Sequential saves use the last successful write.</summary>
public class UpdatePlayerProgressRequest
{
    [Required, MaxLength(10000)]
    public List<Guid> UnlockedVehicleIds { get; init; } = null!;
    [Required]
    public PlayerCalculatorPreferences Preferences { get; init; } = null!;

    public UpdatePlayerProgressRequest() { }
    public UpdatePlayerProgressRequest(List<Guid> unlockedVehicleIds, PlayerCalculatorPreferences preferences)
        => (UnlockedVehicleIds, Preferences) = (unlockedVehicleIds, preferences);
}

public record PlayerProgressResponse(List<Guid> UnlockedVehicleIds, PlayerCalculatorPreferences Preferences);

public record PlayerRegistrationResult(PlayerTokenResponse? Account, string? Error = null, bool Conflict = false);
public record PlayerProgressUpdateResult(PlayerProgressResponse? Progress, string? Error = null, bool NotFound = false, bool Conflict = false);
