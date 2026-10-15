using GrindingThunder.Api.Application.Exceptions;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Domain.Services;
using GrindingThunder.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GrindingThunder.Api.Infrastructure.Services;

/// <summary>
/// Domain service implementing the two-phase (Validate/Preview and Commit) tabular import workflow
/// for Draft ResearchTreeVersions.
/// </summary>
public class TechTreeImportService(ApplicationDbContext context) : ITechTreeImportService
{
    private readonly ApplicationDbContext _context = context;

    private record ParsedImportRow(
        int RowNumber,
        string VehicleName,
        int RankNumber,
        int RpCost,
        int SlCost,
        int TreeColumn,
        int TreeRow,
        VehicleAcquisitionType AcquisitionType,
        string? FolderParentVehicleName,
        List<string> PrerequisiteVehicleNames,
        int? RequiredVehiclesUnlocked);

    private class ValidationContext
    {
        public List<ParsedImportRow> ValidRows { get; set; } = new();
        public List<ImportEntryPreviewDto> PreviewEntries { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public HashSet<string> AutoCreatedVehicles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<int> CreatedRanks { get; set; } = new();
        public int NewEntriesCount { get; set; }
        public int UpdatedEntriesCount { get; set; }
    }

    public async Task<TechTreeImportPreviewDto> PreviewImportAsync(
        Guid versionId,
        Stream fileStream,
        string fileName,
        bool autoCreateMissingVehicles = true,
        CancellationToken cancellationToken = default)
    {
        var version = await EnsureDraftVersionAsync(versionId, cancellationToken);
        var parseResult = TabularDataParser.Parse(fileStream, fileName);

        if (parseResult.Errors.Count > 0)
        {
            return new TechTreeImportPreviewDto(
                IsValid: false,
                TotalRowsDetected: 0,
                NewEntriesCount: 0,
                UpdatedEntriesCount: 0,
                AutoCreatedVehiclesCount: 0,
                CreatedRanksCount: 0,
                Entries: new List<ImportEntryPreviewDto>(),
                Errors: parseResult.Errors,
                Warnings: new List<string>());
        }

        var valContext = await ValidateImportDataAsync(
            version, parseResult.Rows, autoCreateMissingVehicles, cancellationToken);

        return new TechTreeImportPreviewDto(
            IsValid: valContext.Errors.Count == 0,
            TotalRowsDetected: parseResult.Rows.Count,
            NewEntriesCount: valContext.NewEntriesCount,
            UpdatedEntriesCount: valContext.UpdatedEntriesCount,
            AutoCreatedVehiclesCount: valContext.AutoCreatedVehicles.Count,
            CreatedRanksCount: valContext.CreatedRanks.Count,
            Entries: valContext.PreviewEntries,
            Errors: valContext.Errors,
            Warnings: valContext.Warnings);
    }

    public async Task<TechTreeImportCommitResultDto> CommitImportAsync(
        Guid versionId,
        Stream fileStream,
        string fileName,
        bool autoCreateMissingVehicles = true,
        CancellationToken cancellationToken = default)
    {
        var version = await EnsureDraftVersionAsync(versionId, cancellationToken);
        var parseResult = TabularDataParser.Parse(fileStream, fileName);

        if (parseResult.Errors.Count > 0)
        {
            return new TechTreeImportCommitResultDto(
                Success: false,
                ImportedEntriesCount: 0,
                NewEntriesCount: 0,
                UpdatedEntriesCount: 0,
                AutoCreatedVehiclesCount: 0,
                CreatedRanksCount: 0,
                Messages: new List<string> { "Import validation failed due to file parsing errors." },
                Errors: parseResult.Errors);
        }

        var valContext = await ValidateImportDataAsync(
            version, parseResult.Rows, autoCreateMissingVehicles, cancellationToken);

        if (valContext.Errors.Count > 0)
        {
            return new TechTreeImportCommitResultDto(
                Success: false,
                ImportedEntriesCount: 0,
                NewEntriesCount: 0,
                UpdatedEntriesCount: 0,
                AutoCreatedVehiclesCount: 0,
                CreatedRanksCount: 0,
                Messages: new List<string> { "Import validation failed." },
                Errors: valContext.Errors);
        }

        // Execute atomic commit transaction
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // 1. Auto-create missing vehicles in Vehicle catalog
            var catalogVehicles = await _context.Vehicles
                .Where(v => v.ResearchTreeId == version.ResearchTreeId).ToListAsync(cancellationToken);
            var catalogMap = catalogVehicles.ToDictionary(v => v.Name.Trim().ToLowerInvariant(), v => v);

            if (autoCreateMissingVehicles && valContext.AutoCreatedVehicles.Count > 0)
            {
                foreach (var missingName in valContext.AutoCreatedVehicles)
                {
                    var lower = missingName.Trim().ToLowerInvariant();
                    if (!catalogMap.ContainsKey(lower))
                    {
                        var newVehicle = new Vehicle
                        {
                            Id = Guid.NewGuid(),
                            ResearchTreeId = version.ResearchTreeId,
                            Name = missingName.Trim(),
                            ImageUrl = null
                        };
                        _context.Vehicles.Add(newVehicle);
                        catalogMap[lower] = newVehicle;
                    }
                }
                await _context.SaveChangesAsync(cancellationToken);
            }

            // 2. Ensure TreeRanks exist in the draft version
            var existingRanks = await _context.TreeRanks
                .Where(r => r.ResearchTreeVersionId == versionId)
                .ToListAsync(cancellationToken);
            var rankMap = existingRanks.ToDictionary(r => r.RankNumber, r => r);

            // Group import rows by rank to extract any specified quotas
            var rankQuotas = valContext.ValidRows
                .Where(r => r.RequiredVehiclesUnlocked.HasValue)
                .GroupBy(r => r.RankNumber)
                .ToDictionary(g => g.Key, g => g.Last().RequiredVehiclesUnlocked!.Value);

            var neededRankNumbers = valContext.ValidRows.Select(r => r.RankNumber).Distinct().OrderBy(r => r);
            foreach (var rankNum in neededRankNumbers)
            {
                if (!rankMap.TryGetValue(rankNum, out var rank))
                {
                    rank = new TreeRank
                    {
                        Id = Guid.NewGuid(),
                        ResearchTreeVersionId = versionId,
                        RankNumber = rankNum,
                        RequiredVehiclesUnlocked = rankQuotas.GetValueOrDefault(rankNum, 0)
                    };
                    _context.TreeRanks.Add(rank);
                    rankMap[rankNum] = rank;
                }
                else if (rankQuotas.TryGetValue(rankNum, out var quota))
                {
                    rank.RequiredVehiclesUnlocked = quota;
                }
            }
            await _context.SaveChangesAsync(cancellationToken);

            // 3. Upsert VehicleTreeEntries (Pass 1: without FolderParentEntryId)
            var existingEntries = await _context.VehicleTreeEntries
                .Include(e => e.Vehicle)
                .Where(e => e.ResearchTreeVersionId == versionId)
                .ToListAsync(cancellationToken);
            var entryByVehicleName = existingEntries.ToDictionary(
                e => e.Vehicle.Name.Trim().ToLowerInvariant(), e => e);

            foreach (var row in valContext.ValidRows)
            {
                var vehicle = catalogMap[row.VehicleName.Trim().ToLowerInvariant()];
                var rank = rankMap[row.RankNumber];

                if (entryByVehicleName.TryGetValue(row.VehicleName.Trim().ToLowerInvariant(), out var entry))
                {
                    // Update existing draft entry
                    entry.TreeRankId = rank.Id;
                    entry.RpCost = row.RpCost;
                    entry.SlCost = row.SlCost;
                    entry.TreeColumn = row.TreeColumn;
                    entry.TreeRow = row.TreeRow;
                    entry.AcquisitionType = row.AcquisitionType;
                    entry.FolderParentEntryId = null; // reset, linked in Pass 2
                }
                else
                {
                    // Create new entry
                    entry = new VehicleTreeEntry
                    {
                        Id = Guid.NewGuid(),
                        ResearchTreeId = version.ResearchTreeId,
                        ResearchTreeVersionId = versionId,
                        VehicleId = vehicle.Id,
                        TreeRankId = rank.Id,
                        RpCost = row.RpCost,
                        SlCost = row.SlCost,
                        TreeColumn = row.TreeColumn,
                        TreeRow = row.TreeRow,
                        AcquisitionType = row.AcquisitionType,
                        FolderParentEntryId = null
                    };
                    _context.VehicleTreeEntries.Add(entry);
                    entryByVehicleName[row.VehicleName.Trim().ToLowerInvariant()] = entry;
                }
            }
            await _context.SaveChangesAsync(cancellationToken);

            // 4. Pass 2: Link FolderParentEntryId
            foreach (var row in valContext.ValidRows)
            {
                var entry = entryByVehicleName[row.VehicleName.Trim().ToLowerInvariant()];
                if (!string.IsNullOrWhiteSpace(row.FolderParentVehicleName))
                {
                    var parentEntry = entryByVehicleName[row.FolderParentVehicleName.Trim().ToLowerInvariant()];
                    entry.FolderParentEntryId = parentEntry.Id;
                }
                else
                {
                    entry.FolderParentEntryId = null;
                }
            }
            await _context.SaveChangesAsync(cancellationToken);

            // 5. Update Prerequisite edges for imported entries
            var importedEntryIds = valContext.ValidRows
                .Select(r => entryByVehicleName[r.VehicleName.Trim().ToLowerInvariant()].Id)
                .ToHashSet();

            // Remove existing prerequisites where the imported entry is the child (dependent)
            var existingPrereqsToRemove = await _context.VehiclePrerequisites
                .Where(p => p.ResearchTreeVersionId == versionId && importedEntryIds.Contains(p.VehicleTreeEntryId))
                .ToListAsync(cancellationToken);
            _context.VehiclePrerequisites.RemoveRange(existingPrereqsToRemove);
            await _context.SaveChangesAsync(cancellationToken);

            // Add new prerequisites from rows
            foreach (var row in valContext.ValidRows)
            {
                var entry = entryByVehicleName[row.VehicleName.Trim().ToLowerInvariant()];
                foreach (var prereqName in row.PrerequisiteVehicleNames)
                {
                    var prereqEntry = entryByVehicleName[prereqName.Trim().ToLowerInvariant()];
                    var edge = new VehiclePrerequisite
                    {
                        ResearchTreeVersionId = versionId,
                        VehicleTreeEntryId = entry.Id,
                        PrerequisiteVehicleTreeEntryId = prereqEntry.Id
                    };
                    _context.VehiclePrerequisites.Add(edge);
                }
            }
            await _context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new TechTreeImportCommitResultDto(
                Success: true,
                ImportedEntriesCount: valContext.ValidRows.Count,
                NewEntriesCount: valContext.NewEntriesCount,
                UpdatedEntriesCount: valContext.UpdatedEntriesCount,
                AutoCreatedVehiclesCount: valContext.AutoCreatedVehicles.Count,
                CreatedRanksCount: valContext.CreatedRanks.Count,
                Messages: new List<string>
                {
                    $"Successfully imported {valContext.ValidRows.Count} entries " +
                    $"({valContext.NewEntriesCount} new, {valContext.UpdatedEntriesCount} updated) " +
                    $"into draft version '{versionId}'."
                });
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<ResearchTreeVersion> EnsureDraftVersionAsync(Guid versionId, CancellationToken cancellationToken)
    {
        var version = await _context.ResearchTreeVersions
            .FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken);

        if (version is null)
        {
            throw new ResearchTreeVersionNotFoundException(versionId);
        }

        if (version.Status != ResearchTreeVersionStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Research tree version '{versionId}' is not in Draft status (status: {version.Status}). Published versions are immutable.");
        }

        return version;
    }

    private async Task<ValidationContext> ValidateImportDataAsync(
        ResearchTreeVersion version,
        List<TabularRow> rawRows,
        bool autoCreateMissingVehicles,
        CancellationToken cancellationToken)
    {
        var valContext = new ValidationContext();

        // 1. Load catalog vehicles and existing draft entities
        var catalogVehicles = await _context.Vehicles.AsNoTracking()
            .Where(v => v.ResearchTreeId == version.ResearchTreeId).ToListAsync(cancellationToken);
        var catalogMap = catalogVehicles.ToDictionary(v => v.Name.Trim().ToLowerInvariant(), v => v);

        var existingRanks = await _context.TreeRanks.AsNoTracking()
            .Where(r => r.ResearchTreeVersionId == version.Id)
            .ToListAsync(cancellationToken);
        var existingRankMap = existingRanks.ToDictionary(r => r.RankNumber, r => r);

        var existingEntries = await _context.VehicleTreeEntries.AsNoTracking()
            .Include(e => e.Vehicle)
            .Where(e => e.ResearchTreeVersionId == version.Id)
            .ToListAsync(cancellationToken);
        var existingEntryByVehicle = existingEntries.ToDictionary(
            e => e.Vehicle.Name.Trim().ToLowerInvariant(), e => e);

        var existingPrereqs = await _context.VehiclePrerequisites.AsNoTracking()
            .Where(p => p.ResearchTreeVersionId == version.Id)
            .ToListAsync(cancellationToken);

        // 2. Parse individual rows and data types
        var parsedRows = new List<ParsedImportRow>();
        var seenVehicleNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rawRows)
        {
            bool rowValid = true;

            // VehicleName
            var vehicleName = row.Values.GetValueOrDefault("VehicleName", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(vehicleName))
            {
                valContext.Errors.Add($"Row {row.RowNumber}: VehicleName cannot be empty.");
                rowValid = false;
            }
            else if (seenVehicleNames.TryGetValue(vehicleName, out var prevRow))
            {
                valContext.Errors.Add($"Row {row.RowNumber}: Duplicate vehicle '{vehicleName}' found in import file (previously defined at row {prevRow}).");
                rowValid = false;
            }
            else
            {
                seenVehicleNames[vehicleName] = row.RowNumber;
            }

            // RankNumber
            var rankStr = row.Values.GetValueOrDefault("RankNumber", string.Empty).Trim();
            if (!TryParseRank(rankStr, out int rankNumber))
            {
                valContext.Errors.Add($"Row {row.RowNumber}: Invalid RankNumber '{rankStr}'. Must be a positive integer or roman numeral.");
                rowValid = false;
            }

            // RpCost
            var rpStr = row.Values.GetValueOrDefault("RpCost", string.Empty).Trim();
            if (!TryParseCost(rpStr, out int rpCost))
            {
                valContext.Errors.Add($"Row {row.RowNumber}: Invalid RpCost '{rpStr}'. Must be a non-negative integer.");
                rowValid = false;
            }

            // SlCost
            var slStr = row.Values.GetValueOrDefault("SlCost", string.Empty).Trim();
            if (!TryParseCost(slStr, out int slCost))
            {
                valContext.Errors.Add($"Row {row.RowNumber}: Invalid SlCost '{slStr}'. Must be a non-negative integer.");
                rowValid = false;
            }

            // TreeColumn
            var colStr = row.Values.GetValueOrDefault("TreeColumn", string.Empty).Trim();
            if (!int.TryParse(colStr, out int treeColumn) || treeColumn < 0)
            {
                valContext.Errors.Add($"Row {row.RowNumber}: Invalid TreeColumn '{colStr}'. Must be a non-negative integer.");
                rowValid = false;
            }

            // TreeRow
            var rowStr = row.Values.GetValueOrDefault("TreeRow", string.Empty).Trim();
            if (!int.TryParse(rowStr, out int treeRow) || treeRow < 0)
            {
                valContext.Errors.Add($"Row {row.RowNumber}: Invalid TreeRow '{rowStr}'. Must be a non-negative integer.");
                rowValid = false;
            }

            // AcquisitionType (optional)
            var acqStr = row.Values.GetValueOrDefault("AcquisitionType", string.Empty).Trim();
            if (!TryParseAcquisitionType(acqStr, out var acquisitionType))
            {
                valContext.Errors.Add($"Row {row.RowNumber}: Invalid AcquisitionType '{acqStr}'. Allowed values: Researchable, Premium, Squadron, Event, Pack, Hidden.");
                rowValid = false;
            }

            // FolderParentVehicleName (optional)
            var folderParent = row.Values.GetValueOrDefault("FolderParentVehicleName", string.Empty).Trim();
            var folderParentName = string.IsNullOrWhiteSpace(folderParent) ? null : folderParent;

            // PrerequisiteVehicleNames (optional)
            var prereqStr = row.Values.GetValueOrDefault("PrerequisiteVehicleNames", string.Empty).Trim();
            var prereqs = ParsePrerequisites(prereqStr);

            // RequiredVehiclesUnlocked (optional)
            int? rankQuota = null;
            if (row.Values.TryGetValue("RequiredVehiclesUnlocked", out var quotaStr) && !string.IsNullOrWhiteSpace(quotaStr))
            {
                if (int.TryParse(quotaStr.Trim(), out int q) && q >= 0)
                {
                    rankQuota = q;
                }
                else
                {
                    valContext.Errors.Add($"Row {row.RowNumber}: Invalid RequiredVehiclesUnlocked '{quotaStr}'. Must be a non-negative integer.");
                    rowValid = false;
                }
            }

            if (rowValid)
            {
                parsedRows.Add(new ParsedImportRow(
                    row.RowNumber,
                    vehicleName,
                    rankNumber,
                    rpCost,
                    slCost,
                    treeColumn,
                    treeRow,
                    acquisitionType,
                    folderParentName,
                    prereqs,
                    rankQuota));
            }
        }

        valContext.ValidRows = parsedRows;

        // 3. Match against catalog & check existing entries
        var fileVehicleMap = parsedRows.ToDictionary(r => r.VehicleName.Trim().ToLowerInvariant(), r => r);

        foreach (var row in parsedRows)
        {
            var lowerName = row.VehicleName.Trim().ToLowerInvariant();
            bool inCatalog = catalogMap.ContainsKey(lowerName);

            if (!inCatalog)
            {
                if (autoCreateMissingVehicles)
                {
                    valContext.AutoCreatedVehicles.Add(row.VehicleName.Trim());
                    valContext.Warnings.Add($"Vehicle '{row.VehicleName}' was not found in the vehicle catalog and will be auto-created.");
                }
                else
                {
                    valContext.Errors.Add($"Row {row.RowNumber}: Vehicle '{row.VehicleName}' does not exist in the vehicle catalog.");
                }
            }

            bool existsInDraft = existingEntryByVehicle.ContainsKey(lowerName);
            string action = existsInDraft ? "Update" : "Create";

            if (existsInDraft)
            {
                valContext.UpdatedEntriesCount++;
            }
            else
            {
                valContext.NewEntriesCount++;
            }

            valContext.PreviewEntries.Add(new ImportEntryPreviewDto(
                row.RowNumber,
                row.VehicleName,
                row.RankNumber,
                row.RpCost,
                row.SlCost,
                row.TreeColumn,
                row.TreeRow,
                row.AcquisitionType,
                row.FolderParentVehicleName,
                row.PrerequisiteVehicleNames,
                action,
                inCatalog));
        }

        // 4. Check TreeRank creation
        var fileRankNumbers = parsedRows.Select(r => r.RankNumber).Distinct().OrderBy(r => r);
        foreach (var rankNum in fileRankNumbers)
        {
            if (!existingRankMap.ContainsKey(rankNum))
            {
                valContext.CreatedRanks.Add(rankNum);
                valContext.Warnings.Add($"Rank {rankNum} does not exist in draft version '{version.Id}' and will be created.");
            }
        }

        // 5. Root Coordinate Collisions
        // (a) Intra-file root collisions
        var importedRootRows = parsedRows.Where(r => string.IsNullOrWhiteSpace(r.FolderParentVehicleName)).ToList();
        var intraFileCollisions = importedRootRows
            .GroupBy(r => (r.TreeColumn, r.TreeRow))
            .Where(g => g.Count() > 1);

        foreach (var group in intraFileCollisions)
        {
            var names = string.Join(", ", group.Select(r => r.VehicleName).OrderBy(n => n));
            valContext.Errors.Add($"Coordinate collision at ({group.Key.TreeColumn}, {group.Key.TreeRow}) between imported root vehicles: {names}.");
        }

        // (b) Collision with unimported existing root entries in the draft
        var unimportedExistingRoots = existingEntries
            .Where(e => e.FolderParentEntryId == null && !fileVehicleMap.ContainsKey(e.Vehicle.Name.Trim().ToLowerInvariant()))
            .ToList();

        foreach (var rootRow in importedRootRows)
        {
            var collidingExisting = unimportedExistingRoots
                .FirstOrDefault(e => e.TreeColumn == rootRow.TreeColumn && e.TreeRow == rootRow.TreeRow);

            if (collidingExisting != null)
            {
                valContext.Errors.Add(
                    $"Coordinate collision at ({rootRow.TreeColumn}, {rootRow.TreeRow}) between imported vehicle '{rootRow.VehicleName}' and existing draft vehicle '{collidingExisting.Vehicle.Name}'.");
            }
        }

        // 6. Folder Hierarchy Validation
        foreach (var row in parsedRows.Where(r => !string.IsNullOrWhiteSpace(r.FolderParentVehicleName)))
        {
            var parentName = row.FolderParentVehicleName!.Trim();

            // Self-parent
            if (parentName.Equals(row.VehicleName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                valContext.Errors.Add($"Row {row.RowNumber}: Vehicle '{row.VehicleName}' cannot be its own folder parent.");
                continue;
            }

            // Existence in file or draft
            var fileParent = fileVehicleMap.GetValueOrDefault(parentName.ToLowerInvariant());
            var draftParent = existingEntryByVehicle.GetValueOrDefault(parentName.ToLowerInvariant());

            if (fileParent is null && draftParent is null)
            {
                valContext.Errors.Add($"Row {row.RowNumber}: Folder parent '{parentName}' for vehicle '{row.VehicleName}' does not exist in the import file or draft version.");
                continue;
            }

            // Max 1 level of grouping: parent cannot have a folder parent
            if (fileParent != null && !string.IsNullOrWhiteSpace(fileParent.FolderParentVehicleName))
            {
                valContext.Errors.Add($"Row {row.RowNumber}: Folder parent '{parentName}' cannot itself have a folder parent.");
            }
            else if (fileParent is null && draftParent != null && draftParent.FolderParentEntryId != null)
            {
                valContext.Errors.Add($"Row {row.RowNumber}: Existing folder parent '{parentName}' cannot itself have a folder parent.");
            }

            // Same rank rule
            int parentRank = fileParent?.RankNumber
                ?? existingRanks.FirstOrDefault(r => r.Id == draftParent!.TreeRankId)?.RankNumber
                ?? 0;

            if (parentRank > 0 && parentRank != row.RankNumber)
            {
                valContext.Errors.Add($"Row {row.RowNumber}: Folder child '{row.VehicleName}' (Rank {row.RankNumber}) and folder parent '{parentName}' (Rank {parentRank}) must belong to the same rank.");
            }
        }

        // Folder cycles check
        foreach (var row in parsedRows.Where(r => !string.IsNullOrWhiteSpace(r.FolderParentVehicleName)))
        {
            var visitedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { row.VehicleName.Trim() };
            var currentParentName = row.FolderParentVehicleName?.Trim();

            while (!string.IsNullOrWhiteSpace(currentParentName))
            {
                if (!visitedFolders.Add(currentParentName))
                {
                    valContext.Errors.Add($"Folder cycle detected involving '{row.VehicleName}' and '{currentParentName}'.");
                    break;
                }

                var nextFileParent = fileVehicleMap.GetValueOrDefault(currentParentName.ToLowerInvariant());
                if (nextFileParent != null)
                {
                    currentParentName = nextFileParent.FolderParentVehicleName?.Trim();
                }
                else
                {
                    var nextDraftParent = existingEntryByVehicle.GetValueOrDefault(currentParentName.ToLowerInvariant());
                    if (nextDraftParent?.FolderParentEntryId != null)
                    {
                        var parentEntity = existingEntries.FirstOrDefault(e => e.Id == nextDraftParent.FolderParentEntryId);
                        currentParentName = parentEntity?.Vehicle.Name.Trim();
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        // 7. Prerequisite Integrity
        foreach (var row in parsedRows)
        {
            foreach (var prereqName in row.PrerequisiteVehicleNames)
            {
                // Self-dependency
                if (prereqName.Equals(row.VehicleName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    valContext.Errors.Add($"Row {row.RowNumber}: Vehicle '{row.VehicleName}' cannot have itself as a prerequisite.");
                    continue;
                }

                // Existence
                var filePrereq = fileVehicleMap.GetValueOrDefault(prereqName.ToLowerInvariant());
                var draftPrereq = existingEntryByVehicle.GetValueOrDefault(prereqName.ToLowerInvariant());

                if (filePrereq is null && draftPrereq is null)
                {
                    valContext.Errors.Add($"Row {row.RowNumber}: Prerequisite '{prereqName}' for vehicle '{row.VehicleName}' does not exist in the import file or draft version.");
                    continue;
                }

                // Rank progression: prereq rank <= entry rank
                int prereqRank = filePrereq?.RankNumber
                    ?? existingRanks.FirstOrDefault(r => r.Id == draftPrereq!.TreeRankId)?.RankNumber
                    ?? 0;

                if (prereqRank > row.RankNumber)
                {
                    valContext.Errors.Add($"Row {row.RowNumber}: Prerequisite '{prereqName}' (Rank {prereqRank}) cannot belong to a higher rank than '{row.VehicleName}' (Rank {row.RankNumber}).");
                }

                // Acquisition progression integrity: researchable cannot depend on non-researchable
                var prereqAcq = filePrereq?.AcquisitionType ?? draftPrereq!.AcquisitionType;
                if (row.AcquisitionType == VehicleAcquisitionType.Researchable && prereqAcq != VehicleAcquisitionType.Researchable)
                {
                    valContext.Errors.Add($"Row {row.RowNumber}: Researchable vehicle '{row.VehicleName}' cannot have non-researchable prerequisite '{prereqName}' ({prereqAcq}).");
                }
            }
        }

        // 8. Prerequisite DAG Cycle Detection
        var adj = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // Nodes from import
        foreach (var row in parsedRows)
        {
            adj[row.VehicleName.Trim()] = row.PrerequisiteVehicleNames.Select(p => p.Trim()).ToList();
        }

        // Nodes from existing entries not in import
        var entryIdToVehicleName = existingEntries.ToDictionary(e => e.Id, e => e.Vehicle.Name.Trim());
        foreach (var entry in existingEntries)
        {
            var name = entry.Vehicle.Name.Trim();
            if (!adj.ContainsKey(name))
            {
                var entryPrereqs = existingPrereqs
                    .Where(p => p.VehicleTreeEntryId == entry.Id)
                    .Select(p => entryIdToVehicleName.GetValueOrDefault(p.PrerequisiteVehicleTreeEntryId, string.Empty))
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();
                adj[name] = entryPrereqs;
            }
        }

        var dfsState = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var dfsPath = new List<string>();
        var detectedCycles = new HashSet<string>();

        void Dfs(string u)
        {
            dfsState[u] = 1; // visiting
            dfsPath.Add(u);

            if (adj.TryGetValue(u, out var neighbors))
            {
                foreach (var v in neighbors)
                {
                    dfsState.TryGetValue(v, out var vState);
                    if (vState == 1)
                    {
                        var cycleStartIndex = dfsPath.FindIndex(node => node.Equals(v, StringComparison.OrdinalIgnoreCase));
                        var cycleNodes = dfsPath.Skip(cycleStartIndex).Append(v).ToList();
                        var cycleKey = string.Join(" -> ", cycleNodes);
                        if (detectedCycles.Add(cycleKey))
                        {
                            valContext.Errors.Add($"Prerequisite cycle detected: {cycleKey}.");
                        }
                    }
                    else if (vState == 0 && adj.ContainsKey(v))
                    {
                        Dfs(v);
                    }
                }
            }

            dfsPath.RemoveAt(dfsPath.Count - 1);
            dfsState[u] = 2; // visited
        }

        foreach (var node in adj.Keys)
        {
            if (dfsState.GetValueOrDefault(node) == 0)
            {
                Dfs(node);
            }
        }

        // 9. Rank Gate Feasibility Check
        // Group all projected vehicles by rank number
        var projectedVehiclesByRank = new Dictionary<int, int>();

        // (a) From valid rows
        foreach (var row in parsedRows)
        {
            projectedVehiclesByRank[row.RankNumber] = projectedVehiclesByRank.GetValueOrDefault(row.RankNumber, 0) + 1;
        }

        // (b) From unimported existing entries
        foreach (var entry in existingEntries)
        {
            if (!fileVehicleMap.ContainsKey(entry.Vehicle.Name.Trim().ToLowerInvariant()))
            {
                var rank = existingRanks.FirstOrDefault(r => r.Id == entry.TreeRankId);
                if (rank != null)
                {
                    projectedVehiclesByRank[rank.RankNumber] = projectedVehiclesByRank.GetValueOrDefault(rank.RankNumber, 0) + 1;
                }
            }
        }

        // Check each rank quota
        var allRanksToCheck = existingRanks.ToDictionary(r => r.RankNumber, r => r.RequiredVehiclesUnlocked);
        foreach (var row in parsedRows.Where(r => r.RequiredVehiclesUnlocked.HasValue))
        {
            allRanksToCheck[row.RankNumber] = row.RequiredVehiclesUnlocked!.Value;
        }

        var allKnownRankNumbers = projectedVehiclesByRank.Keys.Union(existingRanks.Select(r => r.RankNumber)).ToList();

        foreach (var (rankNum, quota) in allRanksToCheck.OrderBy(r => r.Key))
        {
            if (quota <= 0) continue;

            var precedingRanks = allKnownRankNumbers.Where(r => r < rankNum).ToList();
            int availableVehicles;

            if (precedingRanks.Count > 0)
            {
                availableVehicles = precedingRanks.Sum(r => projectedVehiclesByRank.GetValueOrDefault(r, 0));
            }
            else
            {
                availableVehicles = projectedVehiclesByRank.GetValueOrDefault(rankNum, 0);
            }

            if (availableVehicles < quota)
            {
                valContext.Errors.Add($"Rank {rankNum} requires {quota} unlocked vehicles, but only {availableVehicles} eligible vehicles are available.");
            }
        }

        return valContext;
    }

    private static bool TryParseRank(string text, out int rank)
    {
        text = text.Trim().ToUpperInvariant();
        if (int.TryParse(text, out rank) && rank > 0) return true;

        rank = text switch
        {
            "I" => 1, "II" => 2, "III" => 3, "IV" => 4, "V" => 5,
            "VI" => 6, "VII" => 7, "VIII" => 8, "IX" => 9, "X" => 10,
            _ => 0
        };
        return rank > 0;
    }

    private static bool TryParseCost(string text, out int cost)
    {
        var clean = text.Trim()
            .Replace(",", "")
            .Replace(" ", "")
            .Replace("SL", "", StringComparison.OrdinalIgnoreCase)
            .Replace("RP", "", StringComparison.OrdinalIgnoreCase);

        return int.TryParse(clean, out cost) && cost >= 0;
    }

    private static bool TryParseAcquisitionType(string text, out VehicleAcquisitionType acq)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            acq = VehicleAcquisitionType.Researchable;
            return true;
        }

        var clean = text.Trim()
            .Replace(" ", "")
            .Replace("_", "")
            .Replace("-", "");

        if (int.TryParse(clean, out var intVal) && Enum.IsDefined(typeof(VehicleAcquisitionType), intVal))
        {
            acq = (VehicleAcquisitionType)intVal;
            return true;
        }

        if (Enum.TryParse<VehicleAcquisitionType>(clean, true, out acq))
        {
            return true;
        }

        if (clean.Equals("techtree", StringComparison.OrdinalIgnoreCase)) { acq = VehicleAcquisitionType.Researchable; return true; }
        if (clean.Equals("storepack", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("storepack/bundle", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("bundle", StringComparison.OrdinalIgnoreCase))
        {
            acq = VehicleAcquisitionType.Pack;
            return true;
        }
        if (clean.Equals("compare", StringComparison.OrdinalIgnoreCase)) { acq = VehicleAcquisitionType.Hidden; return true; }

        return false;
    }

    private static List<string> ParsePrerequisites(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();

        char[] delimiters = text.Contains(';') ? new[] { ';' } : text.Contains('|') ? new[] { '|' } : new[] { ',' };
        return text.Split(delimiters, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

