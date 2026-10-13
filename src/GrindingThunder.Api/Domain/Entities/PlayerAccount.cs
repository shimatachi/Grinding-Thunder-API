namespace GrindingThunder.Api.Domain.Entities;

/// <summary>Player identity and saved preferences, independent of administrator accounts.</summary>
public class PlayerAccount
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string NormalizedUsername { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public ResearchOptimizationMode OptimizationMode { get; set; } = ResearchOptimizationMode.LeastRp;
    public int? AverageRpPerMatch { get; set; }
    public int? AverageNetSlPerMatch { get; set; }

    // Optimistic concurrency makes overlapping full progress replacements fail atomically.
    public Guid ProgressStamp { get; set; } = Guid.NewGuid();
    public ICollection<PlayerUnlockedVehicle> UnlockedVehicles { get; set; } = new List<PlayerUnlockedVehicle>();
}
