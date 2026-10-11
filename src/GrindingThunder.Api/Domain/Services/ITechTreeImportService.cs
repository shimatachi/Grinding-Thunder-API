using GrindingThunder.Api.Application.Models;

namespace GrindingThunder.Api.Domain.Services;

/// <summary>
/// Domain service governing tabular (CSV/XLSX) import operations for draft research tree versions.
/// Provides a two-phase workflow:
/// 1. Validate & Preview: inspects input data, matches catalog entities, checks constraints without mutating database state.
/// 2. Commit: atomically persists ranks, entries, layout, folder nesting, and prerequisites into a Draft version.
/// </summary>
public interface ITechTreeImportService
{
    /// <summary>
    /// Parses and validates a tabular file against domain and draft-level constraints, returning a structured preview.
    /// Does not mutate database state.
    /// </summary>
    Task<TechTreeImportPreviewDto> PreviewImportAsync(
        Guid versionId,
        Stream fileStream,
        string fileName,
        bool autoCreateMissingVehicles = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically commits validated tabular data into the target draft research tree version.
    /// Fails if the version is not in Draft status or if file validation fails.
    /// </summary>
    Task<TechTreeImportCommitResultDto> CommitImportAsync(
        Guid versionId,
        Stream fileStream,
        string fileName,
        bool autoCreateMissingVehicles = true,
        CancellationToken cancellationToken = default);
}

