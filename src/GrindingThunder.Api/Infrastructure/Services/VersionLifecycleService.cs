using GrindingThunder.Api.Application.Exceptions;
using GrindingThunder.Api.Application.Models;
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
                ResearchTreeId = sourceVersion.ResearchTreeId,
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

        var validationResult = await ValidateVersionAsync(versionId, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new InvalidOperationException(
                $"Cannot publish research tree version '{versionId}' because validation failed: " +
                string.Join(" ", validationResult.Errors));
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

    public async Task<VersionValidationResult> ValidateVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        var version = await dbContext.ResearchTreeVersions
            .AsNoTracking()
            .AsSplitQuery()
            .Include(v => v.TreeRanks)
            .Include(v => v.Entries)
                .ThenInclude(e => e.Vehicle)
            .FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken);

        if (version is null)
        {
            throw new ResearchTreeVersionNotFoundException(versionId);
        }

        var prerequisites = await dbContext.VehiclePrerequisites
            .AsNoTracking()
            .Where(p => p.ResearchTreeVersionId == versionId)
            .ToListAsync(cancellationToken);

        var errors = new List<string>();
        var warnings = new List<string>();

        // 1. Entity & Cost Invariants: minimum counts
        if (version.TreeRanks.Count == 0)
        {
            errors.Add("Research tree version must contain at least one tree rank.");
        }

        if (version.Entries.Count == 0)
        {
            errors.Add("Research tree version must contain at least one vehicle tree entry.");
        }

        var rankMap = version.TreeRanks.ToDictionary(r => r.Id);
        var entryMap = version.Entries.ToDictionary(e => e.Id);

        // 2. Entity & Cost Invariants: ranks, non-negative costs & layout coordinates
        foreach (var entry in version.Entries)
        {
            var vehicleName = entry.Vehicle?.Name ?? entry.VehicleId.ToString();

            if (entry.ResearchTreeId != version.ResearchTreeId ||
                entry.Vehicle?.ResearchTreeId != version.ResearchTreeId)
            {
                errors.Add($"Vehicle '{vehicleName}' does not belong to this version's research tree.");
            }

            if (!rankMap.ContainsKey(entry.TreeRankId))
            {
                errors.Add($"Vehicle tree entry '{entry.Id}' references rank '{entry.TreeRankId}' which is not in this version.");
            }

            if (entry.RpCost < 0)
            {
                errors.Add($"Vehicle '{vehicleName}' has negative RP cost ({entry.RpCost}).");
            }

            if (entry.SlCost < 0)
            {
                errors.Add($"Vehicle '{vehicleName}' has negative SL cost ({entry.SlCost}).");
            }

            if (entry.TreeRow < 0 || entry.TreeColumn < 0)
            {
                errors.Add($"Vehicle '{vehicleName}' has negative layout coordinates ({entry.TreeColumn}, {entry.TreeRow}).");
            }
        }

        // Coordinate collision for root entries:
        // No two root (non-folder-child) entries may share the exact same (TreeRow, TreeColumn) coordinates.
        var rootEntries = version.Entries.Where(e => e.FolderParentEntryId == null).ToList();
        var coordinateGroups = rootEntries
            .GroupBy(e => (e.TreeColumn, e.TreeRow))
            .Where(g => g.Count() > 1);

        foreach (var group in coordinateGroups)
        {
            var vehicleNames = string.Join(", ", group.Select(e => e.Vehicle?.Name ?? e.VehicleId.ToString()).OrderBy(n => n));
            errors.Add($"Multiple root vehicles share coordinates ({group.Key.TreeColumn}, {group.Key.TreeRow}): {vehicleNames}.");
        }

        // 3. Folder Hierarchy Integrity:
        // - Folder parents cannot themselves have a folder parent (maximum 1 level of grouping).
        // - Folder parent and folder child must belong to the same TreeRank.
        // - No folder cycles.
        foreach (var entry in version.Entries.Where(e => e.FolderParentEntryId != null))
        {
            var childName = entry.Vehicle?.Name ?? entry.VehicleId.ToString();

            if (entryMap.TryGetValue(entry.FolderParentEntryId!.Value, out var parentEntry))
            {
                var parentName = parentEntry.Vehicle?.Name ?? parentEntry.VehicleId.ToString();

                if (parentEntry.FolderParentEntryId != null)
                {
                    errors.Add($"Folder parent '{parentName}' cannot itself have a folder parent.");
                }

                if (entry.TreeRankId != parentEntry.TreeRankId)
                {
                    var childRankNumber = rankMap.TryGetValue(entry.TreeRankId, out var cr) ? cr.RankNumber : -1;
                    var parentRankNumber = rankMap.TryGetValue(parentEntry.TreeRankId, out var pr) ? pr.RankNumber : -1;
                    errors.Add($"Folder child '{childName}' (Rank {childRankNumber}) and folder parent '{parentName}' (Rank {parentRankNumber}) must belong to the same rank.");
                }
            }
            else
            {
                errors.Add($"Folder child '{childName}' references unknown folder parent '{entry.FolderParentEntryId}'.");
            }

            // Folder cycle check
            var current = entry;
            var folderVisited = new HashSet<Guid> { current.Id };
            while (current.FolderParentEntryId != null)
            {
                if (!entryMap.TryGetValue(current.FolderParentEntryId.Value, out var nextParent))
                {
                    break;
                }

                if (!folderVisited.Add(nextParent.Id))
                {
                    var parentName = nextParent.Vehicle?.Name ?? nextParent.VehicleId.ToString();
                    errors.Add($"Folder cycle detected between '{childName}' and '{parentName}'.");
                    break;
                }

                current = nextParent;
            }
        }

        // 4. Rank Progression Consistency & Acquisition Rules:
        // - Prerequisite edges cannot point to higher ranks (prereq.TreeRank.RankNumber <= entry.TreeRank.RankNumber).
        // - A Researchable vehicle must not have a non-researchable vehicle as a prerequisite.
        foreach (var prereq in prerequisites)
        {
            if (entryMap.TryGetValue(prereq.VehicleTreeEntryId, out var entry) &&
                entryMap.TryGetValue(prereq.PrerequisiteVehicleTreeEntryId, out var prereqEntry))
            {
                var entryRank = rankMap.TryGetValue(entry.TreeRankId, out var er) ? er.RankNumber : 0;
                var prereqRank = rankMap.TryGetValue(prereqEntry.TreeRankId, out var pr) ? pr.RankNumber : 0;

                if (prereqRank > entryRank)
                {
                    var entryName = entry.Vehicle?.Name ?? entry.VehicleId.ToString();
                    var prereqName = prereqEntry.Vehicle?.Name ?? prereqEntry.VehicleId.ToString();
                    errors.Add($"Prerequisite '{prereqName}' (Rank {prereqRank}) cannot belong to a higher rank than '{entryName}' (Rank {entryRank}).");
                }

                if (entry.AcquisitionType == VehicleAcquisitionType.Researchable &&
                    prereqEntry.AcquisitionType != VehicleAcquisitionType.Researchable)
                {
                    var entryName = entry.Vehicle?.Name ?? entry.VehicleId.ToString();
                    var prereqName = prereqEntry.Vehicle?.Name ?? prereqEntry.VehicleId.ToString();
                    errors.Add($"Researchable vehicle '{entryName}' cannot have non-researchable vehicle '{prereqName}' ({prereqEntry.AcquisitionType}) as a prerequisite.");
                }
            }
        }

        // Rank gate feasibility check:
        // For each rank where RequiredVehiclesUnlocked > 0, the preceding rank(s) must contain
        // at least RequiredVehiclesUnlocked vehicles eligible to count toward rank unlock.
        foreach (var rank in version.TreeRanks.OrderBy(r => r.RankNumber))
        {
            if (rank.RequiredVehiclesUnlocked <= 0)
            {
                continue;
            }

            var precedingRanks = version.TreeRanks.Where(r => r.RankNumber < rank.RankNumber).ToList();
            int availableVehicles;

            if (precedingRanks.Count > 0)
            {
                var precedingRankIds = precedingRanks.Select(r => r.Id).ToHashSet();
                availableVehicles = version.Entries.Count(e => precedingRankIds.Contains(e.TreeRankId));
            }
            else
            {
                // For lowest rank with a threshold, vehicles in that rank itself
                availableVehicles = version.Entries.Count(e => e.TreeRankId == rank.Id);
            }

            if (availableVehicles < rank.RequiredVehiclesUnlocked)
            {
                errors.Add($"Rank {rank.RankNumber} requires {rank.RequiredVehiclesUnlocked} unlocked vehicles, but only {availableVehicles} eligible vehicles are available.");
            }
        }

        // 5. Graph Acyclicity (DAG):
        // Detect cycles in prerequisite graph using DFS (3-state coloring)
        var adj = new Dictionary<Guid, List<Guid>>();
        foreach (var entry in version.Entries)
        {
            adj[entry.Id] = new List<Guid>();
        }

        foreach (var prereq in prerequisites)
        {
            if (adj.ContainsKey(prereq.VehicleTreeEntryId) && entryMap.ContainsKey(prereq.PrerequisiteVehicleTreeEntryId))
            {
                adj[prereq.VehicleTreeEntryId].Add(prereq.PrerequisiteVehicleTreeEntryId);
            }
        }

        var state = new Dictionary<Guid, int>(); // 0: unvisited, 1: visiting, 2: visited
        var path = new List<Guid>();
        var detectedCycles = new HashSet<string>();

        void Dfs(Guid u)
        {
            state[u] = 1;
            path.Add(u);

            if (adj.TryGetValue(u, out var neighbors))
            {
                foreach (var v in neighbors)
                {
                    state.TryGetValue(v, out var vState);
                    if (vState == 1)
                    {
                        var cycleStartIndex = path.IndexOf(v);
                        var cycleNodes = path.Skip(cycleStartIndex).Append(v)
                            .Select(id => entryMap.TryGetValue(id, out var e) ? (e.Vehicle?.Name ?? id.ToString()) : id.ToString())
                            .ToList();
                        var cycleKey = string.Join(" -> ", cycleNodes);
                        if (detectedCycles.Add(cycleKey))
                        {
                            errors.Add($"Prerequisite cycle detected: {cycleKey}.");
                        }
                    }
                    else if (vState == 0)
                    {
                        Dfs(v);
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            state[u] = 2;
        }

        foreach (var entry in version.Entries)
        {
            if (!state.ContainsKey(entry.Id) || state[entry.Id] == 0)
            {
                Dfs(entry.Id);
            }
        }

        return errors.Count == 0
            ? VersionValidationResult.Success(warnings)
            : VersionValidationResult.Failure(errors, warnings);
    }

    public async Task<VersionDiffResult> CompareVersionsAsync(
        Guid sourceVersionId,
        Guid targetVersionId,
        CancellationToken cancellationToken = default)
    {
        var sourceVersion = await dbContext.ResearchTreeVersions
            .AsNoTracking()
            .AsSplitQuery()
            .Include(v => v.GameUpdate)
            .Include(v => v.TreeRanks)
            .Include(v => v.Entries)
                .ThenInclude(e => e.Vehicle)
            .FirstOrDefaultAsync(v => v.Id == sourceVersionId, cancellationToken);

        if (sourceVersion is null)
        {
            throw new ResearchTreeVersionNotFoundException(sourceVersionId);
        }

        var targetVersion = await dbContext.ResearchTreeVersions
            .AsNoTracking()
            .AsSplitQuery()
            .Include(v => v.GameUpdate)
            .Include(v => v.TreeRanks)
            .Include(v => v.Entries)
                .ThenInclude(e => e.Vehicle)
            .FirstOrDefaultAsync(v => v.Id == targetVersionId, cancellationToken);

        if (targetVersion is null)
        {
            throw new ResearchTreeVersionNotFoundException(targetVersionId);
        }

        if (sourceVersion.ResearchTreeId != targetVersion.ResearchTreeId)
        {
            throw new InvalidOperationException("Cannot compare versions belonging to different research trees.");
        }

        var sourcePrerequisites = await dbContext.VehiclePrerequisites
            .AsNoTracking()
            .Where(p => p.ResearchTreeVersionId == sourceVersionId)
            .ToListAsync(cancellationToken);

        var targetPrerequisites = await dbContext.VehiclePrerequisites
            .AsNoTracking()
            .Where(p => p.ResearchTreeVersionId == targetVersionId)
            .ToListAsync(cancellationToken);

        var sourceEntriesByVehicleId = sourceVersion.Entries.ToDictionary(e => e.VehicleId);
        var targetEntriesByVehicleId = targetVersion.Entries.ToDictionary(e => e.VehicleId);

        var sourceEntriesById = sourceVersion.Entries.ToDictionary(e => e.Id);
        var targetEntriesById = targetVersion.Entries.ToDictionary(e => e.Id);

        var sourceRanksById = sourceVersion.TreeRanks.ToDictionary(r => r.Id);
        var targetRanksById = targetVersion.TreeRanks.ToDictionary(r => r.Id);

        Guid? ResolveParentVehicleId(VehicleTreeEntry entry, Dictionary<Guid, VehicleTreeEntry> entriesById)
        {
            if (entry.FolderParentEntryId.HasValue &&
                entriesById.TryGetValue(entry.FolderParentEntryId.Value, out var parentEntry))
            {
                return parentEntry.VehicleId;
            }
            return null;
        }

        // 1. Added vehicles (in target but not source)
        var addedVehicles = targetVersion.Entries
            .Where(e => !sourceEntriesByVehicleId.ContainsKey(e.VehicleId))
            .OrderBy(e => targetRanksById.TryGetValue(e.TreeRankId, out var r) ? r.RankNumber : 0)
            .ThenBy(e => e.TreeColumn)
            .ThenBy(e => e.TreeRow)
            .ThenBy(e => e.Vehicle.Name)
            .Select(e => new VehicleDiffDto
            {
                VehicleId = e.VehicleId,
                Name = e.Vehicle.Name,
                Rank = targetRanksById.TryGetValue(e.TreeRankId, out var r) ? r.RankNumber : 0,
                RpCost = e.RpCost,
                SlCost = e.SlCost,
                AcquisitionType = e.AcquisitionType,
                TreeColumn = e.TreeColumn,
                TreeRow = e.TreeRow,
                FolderParentVehicleId = ResolveParentVehicleId(e, targetEntriesById)
            })
            .ToList();

        // 2. Removed vehicles (in source but not target)
        var removedVehicles = sourceVersion.Entries
            .Where(e => !targetEntriesByVehicleId.ContainsKey(e.VehicleId))
            .OrderBy(e => sourceRanksById.TryGetValue(e.TreeRankId, out var r) ? r.RankNumber : 0)
            .ThenBy(e => e.TreeColumn)
            .ThenBy(e => e.TreeRow)
            .ThenBy(e => e.Vehicle.Name)
            .Select(e => new VehicleDiffDto
            {
                VehicleId = e.VehicleId,
                Name = e.Vehicle.Name,
                Rank = sourceRanksById.TryGetValue(e.TreeRankId, out var r) ? r.RankNumber : 0,
                RpCost = e.RpCost,
                SlCost = e.SlCost,
                AcquisitionType = e.AcquisitionType,
                TreeColumn = e.TreeColumn,
                TreeRow = e.TreeRow,
                FolderParentVehicleId = ResolveParentVehicleId(e, sourceEntriesById)
            })
            .ToList();

        // 3. Modified vehicles (in both with attribute changes)
        var modifiedVehicles = new List<VehicleModifiedDiffDto>();
        foreach (var (vehicleId, targetEntry) in targetEntriesByVehicleId)
        {
            if (!sourceEntriesByVehicleId.TryGetValue(vehicleId, out var sourceEntry))
            {
                continue;
            }

            var oldRankNumber = sourceRanksById.TryGetValue(sourceEntry.TreeRankId, out var sr) ? sr.RankNumber : 0;
            var newRankNumber = targetRanksById.TryGetValue(targetEntry.TreeRankId, out var tr) ? tr.RankNumber : 0;

            var oldParentVehicleId = ResolveParentVehicleId(sourceEntry, sourceEntriesById);
            var newParentVehicleId = ResolveParentVehicleId(targetEntry, targetEntriesById);

            bool isModified = sourceEntry.RpCost != targetEntry.RpCost ||
                              sourceEntry.SlCost != targetEntry.SlCost ||
                              oldRankNumber != newRankNumber ||
                              sourceEntry.AcquisitionType != targetEntry.AcquisitionType ||
                              sourceEntry.TreeColumn != targetEntry.TreeColumn ||
                              sourceEntry.TreeRow != targetEntry.TreeRow ||
                              oldParentVehicleId != newParentVehicleId;

            if (isModified)
            {
                modifiedVehicles.Add(new VehicleModifiedDiffDto
                {
                    VehicleId = vehicleId,
                    Name = targetEntry.Vehicle.Name,
                    OldRpCost = sourceEntry.RpCost,
                    NewRpCost = targetEntry.RpCost,
                    RpDelta = targetEntry.RpCost - sourceEntry.RpCost,
                    OldSlCost = sourceEntry.SlCost,
                    NewSlCost = targetEntry.SlCost,
                    SlDelta = targetEntry.SlCost - sourceEntry.SlCost,
                    OldRankNumber = oldRankNumber,
                    NewRankNumber = newRankNumber,
                    OldAcquisitionType = sourceEntry.AcquisitionType,
                    NewAcquisitionType = targetEntry.AcquisitionType,
                    OldColumn = sourceEntry.TreeColumn,
                    NewColumn = targetEntry.TreeColumn,
                    OldRow = sourceEntry.TreeRow,
                    NewRow = targetEntry.TreeRow,
                    OldParentVehicleId = oldParentVehicleId,
                    NewParentVehicleId = newParentVehicleId
                });
            }
        }

        modifiedVehicles = modifiedVehicles
            .OrderBy(m => m.NewRankNumber)
            .ThenBy(m => m.Name)
            .ToList();

        // 4. Prerequisites diff (edges by vehicle IDs)
        var sourceEdges = sourcePrerequisites
            .Where(p => sourceEntriesById.ContainsKey(p.VehicleTreeEntryId) && sourceEntriesById.ContainsKey(p.PrerequisiteVehicleTreeEntryId))
            .Select(p => new
            {
                VehicleId = sourceEntriesById[p.VehicleTreeEntryId].VehicleId,
                VehicleName = sourceEntriesById[p.VehicleTreeEntryId].Vehicle.Name,
                PrerequisiteVehicleId = sourceEntriesById[p.PrerequisiteVehicleTreeEntryId].VehicleId,
                PrerequisiteVehicleName = sourceEntriesById[p.PrerequisiteVehicleTreeEntryId].Vehicle.Name
            })
            .ToList();

        var targetEdges = targetPrerequisites
            .Where(p => targetEntriesById.ContainsKey(p.VehicleTreeEntryId) && targetEntriesById.ContainsKey(p.PrerequisiteVehicleTreeEntryId))
            .Select(p => new
            {
                VehicleId = targetEntriesById[p.VehicleTreeEntryId].VehicleId,
                VehicleName = targetEntriesById[p.VehicleTreeEntryId].Vehicle.Name,
                PrerequisiteVehicleId = targetEntriesById[p.PrerequisiteVehicleTreeEntryId].VehicleId,
                PrerequisiteVehicleName = targetEntriesById[p.PrerequisiteVehicleTreeEntryId].Vehicle.Name
            })
            .ToList();

        var sourceEdgeSet = sourceEdges
            .Select(e => (e.VehicleId, e.PrerequisiteVehicleId))
            .ToHashSet();

        var targetEdgeSet = targetEdges
            .Select(e => (e.VehicleId, e.PrerequisiteVehicleId))
            .ToHashSet();

        var addedPrerequisites = targetEdges
            .Where(e => !sourceEdgeSet.Contains((e.VehicleId, e.PrerequisiteVehicleId)))
            .OrderBy(e => e.VehicleName)
            .ThenBy(e => e.PrerequisiteVehicleName)
            .Select(e => new PrerequisiteDiffDto
            {
                VehicleId = e.VehicleId,
                VehicleName = e.VehicleName,
                PrerequisiteVehicleId = e.PrerequisiteVehicleId,
                PrerequisiteVehicleName = e.PrerequisiteVehicleName
            })
            .ToList();

        var removedPrerequisites = sourceEdges
            .Where(e => !targetEdgeSet.Contains((e.VehicleId, e.PrerequisiteVehicleId)))
            .OrderBy(e => e.VehicleName)
            .ThenBy(e => e.PrerequisiteVehicleName)
            .Select(e => new PrerequisiteDiffDto
            {
                VehicleId = e.VehicleId,
                VehicleName = e.VehicleName,
                PrerequisiteVehicleId = e.PrerequisiteVehicleId,
                PrerequisiteVehicleName = e.PrerequisiteVehicleName
            })
            .ToList();

        // 5. Rank changes
        var sourceRanksByNumber = sourceVersion.TreeRanks.ToDictionary(r => r.RankNumber);
        var targetRanksByNumber = targetVersion.TreeRanks.ToDictionary(r => r.RankNumber);
        var allRankNumbers = sourceRanksByNumber.Keys.Union(targetRanksByNumber.Keys).OrderBy(n => n).ToList();

        var rankChanges = new List<RankDiffDto>();
        foreach (var rankNum in allRankNumbers)
        {
            var inSource = sourceRanksByNumber.TryGetValue(rankNum, out var sourceRank);
            var inTarget = targetRanksByNumber.TryGetValue(rankNum, out var targetRank);

            if (!inSource && inTarget)
            {
                rankChanges.Add(new RankDiffDto
                {
                    RankNumber = rankNum,
                    OldRequiredVehiclesUnlocked = null,
                    NewRequiredVehiclesUnlocked = targetRank!.RequiredVehiclesUnlocked,
                    ChangeType = RankChangeType.Added
                });
            }
            else if (inSource && !inTarget)
            {
                rankChanges.Add(new RankDiffDto
                {
                    RankNumber = rankNum,
                    OldRequiredVehiclesUnlocked = sourceRank!.RequiredVehiclesUnlocked,
                    NewRequiredVehiclesUnlocked = null,
                    ChangeType = RankChangeType.Removed
                });
            }
            else if (inSource && inTarget && sourceRank!.RequiredVehiclesUnlocked != targetRank!.RequiredVehiclesUnlocked)
            {
                rankChanges.Add(new RankDiffDto
                {
                    RankNumber = rankNum,
                    OldRequiredVehiclesUnlocked = sourceRank!.RequiredVehiclesUnlocked,
                    NewRequiredVehiclesUnlocked = targetRank!.RequiredVehiclesUnlocked,
                    ChangeType = RankChangeType.Modified
                });
            }
        }

        // 6. Summary
        var summary = new VersionDiffSummaryDto
        {
            AddedVehiclesCount = addedVehicles.Count,
            RemovedVehiclesCount = removedVehicles.Count,
            ModifiedVehiclesCount = modifiedVehicles.Count,
            AddedPrerequisitesCount = addedPrerequisites.Count,
            RemovedPrerequisitesCount = removedPrerequisites.Count,
            NetRpDelta = addedVehicles.Sum(v => (long)v.RpCost) - removedVehicles.Sum(v => (long)v.RpCost) + modifiedVehicles.Sum(v => (long)v.RpDelta),
            NetSlDelta = addedVehicles.Sum(v => (long)v.SlCost) - removedVehicles.Sum(v => (long)v.SlCost) + modifiedVehicles.Sum(v => (long)v.SlDelta)
        };

        return new VersionDiffResult
        {
            SourceVersionId = sourceVersionId,
            TargetVersionId = targetVersionId,
            SourceGameUpdateVersion = sourceVersion.GameUpdate?.Version ?? string.Empty,
            TargetGameUpdateVersion = targetVersion.GameUpdate?.Version ?? string.Empty,
            AddedVehicles = addedVehicles,
            RemovedVehicles = removedVehicles,
            ModifiedVehicles = modifiedVehicles,
            AddedPrerequisites = addedPrerequisites,
            RemovedPrerequisites = removedPrerequisites,
            RankChanges = rankChanges,
            Summary = summary
        };
    }
}

