using GrindingThunder.Api.Domain.Entities;

namespace GrindingThunder.Api.Domain.Services;

/// <summary>
/// Domain service governing research tree version lifecycle operations:
/// cloning versions into draft status, transitioning drafts to published,
/// and deleting unneeded drafts.
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
    /// Transitions a Draft research-tree version to Published status, turning it into
    /// an immutable historical snapshot.
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
}

