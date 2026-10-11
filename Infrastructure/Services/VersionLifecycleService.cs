using GrindingThunder.Api.Application.Exceptions;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Domain.Services;
using GrindingThunder.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GrindingThunder.Api.Infrastructure.Services;

public class VersionLifecycleService(ApplicationDbContext dbContext) : IVersionLifecycleService
{
    public async Task<ResearchTreeVersion> CloneVersionAsync(
        Guid sourceVersionId,
        Guid targetGameUpdateId,
        CancellationToken cancellationToken = default)
    {
        var sourceVersion = await dbContext.ResearchTreeVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == sourceVersionId, cancellationToken);

        if (sourceVersion is null)
        {
            throw new ResearchTreeVersionNotFoundException(sourceVersionId);
        }

        var targetGameUpdateExists = await dbContext.GameUpdates
            .AnyAsync(gu => gu.Id == targetGameUpdateId, cancellationToken);

        if (!targetGameUpdateExists)
        {
            throw new InvalidOperationException($"Target game update '{targetGameUpdateId}' was not found.");
        }

        var alreadyExists = await dbContext.ResearchTreeVersions
            .AnyAsync(v => v.ResearchTreeId == sourceVersion.ResearchTreeId && v.GameUpdateId == targetGameUpdateId, cancellationToken);

        if (alreadyExists)
        {
            throw new InvalidOperationException(
                $"A research tree version already exists for research tree '{sourceVersion.ResearchTreeId}' and game update '{targetGameUpdateId}'.");
        }

        var sourceRanks = await dbContext.TreeRanks
            .AsNoTracking()
            .Where(r => r.ResearchTreeVersionId == sourceVersionId)
            .ToListAsync(cancellationToken);

        var sourceEntries = await dbContext.VehicleTreeEntries
            .AsNoTracking()
            .Where(e => e.ResearchTreeVersionId == sourceVersionId)
            .ToListAsync(cancellationToken);

        var sourcePrerequisites = await dbContext.VehiclePrerequisites
            .AsNoTracking()
            .Where(p => p.ResearchTreeVersionId == sourceVersionId)
            .ToListAsync(cancellationToken);

        var clonedVersionId = Guid.NewGuid();
        var clonedVersion = new ResearchTreeVersion
        {
            Id = clonedVersionId,
            ResearchTreeId = sourceVersion.ResearchTreeId,
            GameUpdateId = targetGameUpdateId,
            Status = ResearchTreeVersionStatus.Draft
        };
        dbContext.ResearchTreeVersions.Add(clonedVersion);

        var rankIdMap = new Dictionary<Guid, Guid>();
        var clonedRanks = new List<TreeRank>(sourceRanks.Count);
        foreach (var rank in sourceRanks)
        {
            var newRankId = Guid.NewGuid();
            rankIdMap[rank.Id] = newRankId;
            clonedRanks.Add(new TreeRank
            {
                Id = newRankId,
                ResearchTreeVersionId = clonedVersionId,
                RankNumber = rank.RankNumber,
                RequiredVehiclesUnlocked = rank.RequiredVehiclesUnlocked
            });
        }
        dbContext.TreeRanks.AddRange(clonedRanks);

        var entryIdMap = new Dictionary<Guid, Guid>();
        foreach (var entry in sourceEntries)
        {
            entryIdMap[entry.Id] = Guid.NewGuid();
        }

        var clonedEntries = new List<VehicleTreeEntry>(sourceEntries.Count);
        foreach (var entry in sourceEntries)
        {
            Guid? remappedParentId = null;
            if (entry.FolderParentEntryId.HasValue &&
                entryIdMap.TryGetValue(entry.FolderParentEntryId.Value, out var newParentId))
            {
                remappedParentId = newParentId;
            }

            if (!rankIdMap.TryGetValue(entry.TreeRankId, out var newRankId))
            {
                throw new InvalidOperationException(
                    $"Source vehicle tree entry '{entry.Id}' references an unknown tree rank '{entry.TreeRankId}'.");
            }

            clonedEntries.Add(new VehicleTreeEntry
            {
                Id = entryIdMap[entry.Id],
                ResearchTreeVersionId = clonedVersionId,
                VehicleId = entry.VehicleId,
                TreeRankId = newRankId,
                RpCost = entry.RpCost,
                SlCost = entry.SlCost,
                TreeColumn = entry.TreeColumn,
                TreeRow = entry.TreeRow,
                AcquisitionType = entry.AcquisitionType,
                FolderParentEntryId = remappedParentId
            });
        }
        dbContext.VehicleTreeEntries.AddRange(clonedEntries);

        var clonedPrerequisites = new List<VehiclePrerequisite>(sourcePrerequisites.Count);
        foreach (var prereq in sourcePrerequisites)
        {
            if (!entryIdMap.TryGetValue(prereq.VehicleTreeEntryId, out var newEntryId) ||
                !entryIdMap.TryGetValue(prereq.PrerequisiteVehicleTreeEntryId, out var newPrereqEntryId))
            {
                throw new InvalidOperationException(
                    $"Source prerequisite edge '{prereq.VehicleTreeEntryId} -> {prereq.PrerequisiteVehicleTreeEntryId}' references entries not present in source version.");
            }

            clonedPrerequisites.Add(new VehiclePrerequisite
            {
                ResearchTreeVersionId = clonedVersionId,
                VehicleTreeEntryId = newEntryId,
                PrerequisiteVehicleTreeEntryId = newPrereqEntryId
            });
        }
        dbContext.VehiclePrerequisites.AddRange(clonedPrerequisites);

        clonedVersion.TreeRanks = clonedRanks;
        clonedVersion.Entries = clonedEntries;

        await dbContext.SaveChangesAsync(cancellationToken);
        return clonedVersion;
    }

    public async Task PublishVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        var version = await dbContext.ResearchTreeVersions
            .FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken);

        if (version is null)
        {
            throw new ResearchTreeVersionNotFoundException(versionId);
        }

        if (version.Status == ResearchTreeVersionStatus.Published)
        {
            throw new InvalidOperationException(
                $"Research tree version '{versionId}' is already published.");
        }

        version.Status = ResearchTreeVersionStatus.Published;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteDraftVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        var version = await dbContext.ResearchTreeVersions
            .FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken);

        if (version is null)
        {
            throw new ResearchTreeVersionNotFoundException(versionId);
        }

        if (version.Status == ResearchTreeVersionStatus.Published)
        {
            throw new InvalidOperationException(
                $"Cannot delete research tree version '{versionId}' because it is published.");
        }

        // To ensure clean deletion without self-referential restrict violations on folder parent hierarchy,
        // clear FolderParentEntryId on draft entries if any exist.
        var entriesWithParents = await dbContext.VehicleTreeEntries
            .Where(e => e.ResearchTreeVersionId == versionId && e.FolderParentEntryId != null)
            .ToListAsync(cancellationToken);

        foreach (var entry in entriesWithParents)
        {
            entry.FolderParentEntryId = null;
        }

        if (entriesWithParents.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        dbContext.ResearchTreeVersions.Remove(version);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

