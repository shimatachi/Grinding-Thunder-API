using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Domain.Entities;

namespace GrindingThunder.Api.Domain.Services;

/// <summary>
/// Domain service governing research tree version lifecycle operations:
/// cloning versions into draft status, transitioning drafts to published,
/// deleting unneeded drafts, validating version graph and progression rules,
/// and comparing historical snapshot diffs.
/// </summary>
public interface IVersionLifecycleService
{
    /// <summary>
    /// Deep-clones an existing research-tree version (ranks, entries, layout/folder hierarchies,
    /// and prerequisite graph) into an editable Draft version for a target game update.
    /// </summary>
    Task<ResearchTreeVersion> CloneVersionAsync(
        Guid sourceVersionId,
        Guid targetGameUpdateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions a Draft research-tree version to Published status after validating
    /// its graph acyclicity, rank rules, folder hierarchy, and cost invariants.
    /// </summary>
    Task PublishVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a Draft research-tree version and its associated versioned data.
    /// Published versions cannot be deleted.
    /// </summary>
    Task DeleteDraftVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates graph integrity, rank rules, folder hierarchies, and cost/coordinate invariants
    /// for a research tree version.
    /// </summary>
    Task<VersionValidationResult> ValidateVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares two research tree versions and returns a detailed structural and economic diff
    /// (added/removed/modified vehicles, prerequisite changes, rank changes, and cost deltas).
    /// </summary>
    Task<VersionDiffResult> CompareVersionsAsync(
        Guid sourceVersionId,
        Guid targetVersionId,
        CancellationToken cancellationToken = default);
}

