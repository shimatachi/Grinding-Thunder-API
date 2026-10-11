namespace GrindingThunder.Api.Application.Models;

/// <summary>
/// Result of validating a research tree version for graph acyclicity,
/// rank progression consistency, folder hierarchies, entity/cost invariants,
/// and acquisition rules.
/// </summary>
public class VersionValidationResult
{
    public bool IsValid => Errors.Count == 0;

    public List<string> Errors { get; init; } = new();

    public List<string> Warnings { get; init; } = new();

    public static VersionValidationResult Success(IEnumerable<string>? warnings = null)
    {
        var result = new VersionValidationResult();
        if (warnings is not null)
        {
            result.Warnings.AddRange(warnings);
        }
        return result;
    }

    public static VersionValidationResult Failure(IEnumerable<string> errors, IEnumerable<string>? warnings = null)
    {
        var result = new VersionValidationResult();
        result.Errors.AddRange(errors);
        if (warnings is not null)
        {
            result.Warnings.AddRange(warnings);
        }
        return result;
    }
}

