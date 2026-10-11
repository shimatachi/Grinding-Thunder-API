using System.Reflection;
using GrindingThunder.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GrindingThunder.Api.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Nation> Nations { get; set; } = null!;
    public DbSet<VehicleType> VehicleTypes { get; set; } = null!;
    public DbSet<ResearchTree> ResearchTrees { get; set; } = null!;
    public DbSet<ResearchTreeVersion> ResearchTreeVersions { get; set; } = null!;
    public DbSet<GameUpdate> GameUpdates { get; set; } = null!;
    public DbSet<TreeRank> TreeRanks { get; set; } = null!;
    public DbSet<Vehicle> Vehicles { get; set; } = null!;
    public DbSet<VehicleTreeEntry> VehicleTreeEntries { get; set; } = null!;
    public DbSet<VehiclePrerequisite> VehiclePrerequisites { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    public override int SaveChanges()
    {
        ValidatePublishedVersionImmutability();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidatePublishedVersionImmutability();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await ValidatePublishedVersionImmutabilityAsync(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await ValidatePublishedVersionImmutabilityAsync(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidatePublishedVersionImmutability()
    {
        ValidateVersionEntitiesImmutability();

        var (entriesWithVersions, versionStatusMap, untrackedIds) = CollectChildEntitiesAndVersionStatuses();
        if (entriesWithVersions.Count == 0)
        {
            return;
        }

        if (untrackedIds.Count > 0)
        {
            var publishedIdsFromDb = ResearchTreeVersions.AsNoTracking()
                .Where(v => untrackedIds.Contains(v.Id) && v.Status == ResearchTreeVersionStatus.Published)
                .Select(v => v.Id)
                .ToHashSet();

            foreach (var id in untrackedIds)
            {
                versionStatusMap[id] = publishedIdsFromDb.Contains(id);
            }
        }

        EnsureNoChildMutationsOnPublishedVersions(entriesWithVersions, versionStatusMap);
    }

    private async Task ValidatePublishedVersionImmutabilityAsync(CancellationToken cancellationToken)
    {
        ValidateVersionEntitiesImmutability();

        var (entriesWithVersions, versionStatusMap, untrackedIds) = CollectChildEntitiesAndVersionStatuses();
        if (entriesWithVersions.Count == 0)
        {
            return;
        }

        if (untrackedIds.Count > 0)
        {
            var publishedList = await ResearchTreeVersions.AsNoTracking()
                .Where(v => untrackedIds.Contains(v.Id) && v.Status == ResearchTreeVersionStatus.Published)
                .Select(v => v.Id)
                .ToListAsync(cancellationToken);

            var publishedIdsFromDb = publishedList.ToHashSet();

            foreach (var id in untrackedIds)
            {
                versionStatusMap[id] = publishedIdsFromDb.Contains(id);
            }
        }

        EnsureNoChildMutationsOnPublishedVersions(entriesWithVersions, versionStatusMap);
    }

    private void ValidateVersionEntitiesImmutability()
    {
        foreach (var entry in ChangeTracker.Entries<ResearchTreeVersion>())
        {
            if (entry.State == EntityState.Deleted)
            {
                var originalStatus = entry.OriginalValues.GetValue<ResearchTreeVersionStatus>(nameof(ResearchTreeVersion.Status));
                if (originalStatus == ResearchTreeVersionStatus.Published || entry.Entity.Status == ResearchTreeVersionStatus.Published)
                {
                    throw new InvalidOperationException(
                        $"Published research tree version '{entry.Entity.Id}' is immutable and cannot be deleted.");
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                var originalStatus = entry.OriginalValues.GetValue<ResearchTreeVersionStatus>(nameof(ResearchTreeVersion.Status));
                if (originalStatus == ResearchTreeVersionStatus.Published)
                {
                    throw new InvalidOperationException(
                        $"Published research tree version '{entry.Entity.Id}' is immutable and cannot be modified.");
                }

                if (originalStatus == ResearchTreeVersionStatus.Draft &&
                    entry.Entity.Status == ResearchTreeVersionStatus.Published)
                {
                    var otherModified = entry.Properties.Any(p =>
                        p.IsModified && p.Metadata.Name != nameof(ResearchTreeVersion.Status));

                    if (otherModified)
                    {
                        throw new InvalidOperationException(
                            $"Cannot modify properties other than Status when publishing research tree version '{entry.Entity.Id}'.");
                    }
                }
            }
        }
    }

    private (List<(EntityEntry Entry, Guid VersionId)> EntriesWithVersions,
             Dictionary<Guid, bool> VersionStatusMap,
             List<Guid> UntrackedIds) CollectChildEntitiesAndVersionStatuses()
    {
        var childEntries = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
                        e.Entity is TreeRank or VehicleTreeEntry or VehiclePrerequisite)
            .ToList();

        if (childEntries.Count == 0)
        {
            return ([], new Dictionary<Guid, bool>(), []);
        }

        static Guid GetVersionId(EntityEntry entry) => entry.Entity switch
        {
            TreeRank r => r.ResearchTreeVersionId != Guid.Empty ? r.ResearchTreeVersionId : r.ResearchTreeVersion?.Id ?? Guid.Empty,
            VehicleTreeEntry e => e.ResearchTreeVersionId != Guid.Empty ? e.ResearchTreeVersionId : e.ResearchTreeVersion?.Id ?? Guid.Empty,
            VehiclePrerequisite p => p.ResearchTreeVersionId != Guid.Empty ? p.ResearchTreeVersionId : p.VehicleTreeEntry?.ResearchTreeVersionId ?? p.VehicleTreeEntry?.ResearchTreeVersion?.Id ?? Guid.Empty,
            _ => Guid.Empty
        };

        var entriesWithVersions = new List<(EntityEntry Entry, Guid VersionId)>();
        foreach (var childEntry in childEntries)
        {
            var currentVersionId = GetVersionId(childEntry);
            if (currentVersionId != Guid.Empty)
            {
                entriesWithVersions.Add((childEntry, currentVersionId));
            }

            if (childEntry.State == EntityState.Modified)
            {
                var prop = childEntry.Properties.FirstOrDefault(p => p.Metadata.Name == nameof(VehicleTreeEntry.ResearchTreeVersionId));
                if (prop != null && prop.IsModified && prop.OriginalValue is Guid origGuid && origGuid != Guid.Empty && origGuid != currentVersionId)
                {
                    entriesWithVersions.Add((childEntry, origGuid));
                }
            }
        }

        var versionStatusMap = new Dictionary<Guid, bool>();
        foreach (var versionEntry in ChangeTracker.Entries<ResearchTreeVersion>())
        {
            var wasPublished = versionEntry.State != EntityState.Added &&
                versionEntry.OriginalValues.GetValue<ResearchTreeVersionStatus>(nameof(ResearchTreeVersion.Status)) == ResearchTreeVersionStatus.Published;
            var isPublishedNow = versionEntry.Entity.Status == ResearchTreeVersionStatus.Published;
            versionStatusMap[versionEntry.Entity.Id] = wasPublished || isPublishedNow;
        }

        var untrackedIds = entriesWithVersions
            .Select(x => x.VersionId)
            .Where(id => !versionStatusMap.ContainsKey(id))
            .Distinct()
            .ToList();

        return (entriesWithVersions, versionStatusMap, untrackedIds);
    }

    private static void EnsureNoChildMutationsOnPublishedVersions(
        List<(EntityEntry Entry, Guid VersionId)> entriesWithVersions,
        Dictionary<Guid, bool> versionStatusMap)
    {
        foreach (var (childEntry, versionId) in entriesWithVersions)
        {
            if (versionStatusMap.TryGetValue(versionId, out var isPublished) && isPublished)
            {
                var entityName = childEntry.Entity.GetType().Name;
                throw new InvalidOperationException(
                    $"Cannot {childEntry.State.ToString().ToLowerInvariant()} {entityName}: research tree version '{versionId}' is published and immutable.");
            }
        }
    }
}
