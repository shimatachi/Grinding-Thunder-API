using GrindingThunder.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GrindingThunder.Api.Infrastructure.Persistence.Configurations;

public class VehicleTreeEntryConfiguration : IEntityTypeConfiguration<VehicleTreeEntry>
{
    public void Configure(EntityTypeBuilder<VehicleTreeEntry> builder)
    {
        builder.HasKey(e => e.Id);

        builder.HasOne(e => e.ResearchTreeVersion)
            .WithMany(rtv => rtv.Entries)
            .HasForeignKey(e => e.ResearchTreeVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Vehicle)
            .WithMany(v => v.TreeEntries)
            .HasForeignKey(e => e.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);

        // TRANSITIONAL (Batch 3): Rank is not yet versioned (Batch 4 introduces
        // TreeRank). Rank belongs to a ResearchTree, so deleting a tree cascades
        // through both the version and the rank; keeping this FK cascade too
        // avoids non-deterministic FK-check ordering when a tree is deleted.
        // Deleting a Vehicle never cascades its entries (Restrict above) so
        // historical placements cannot vanish while a version still references them.
        builder.HasOne(e => e.Rank)
            .WithMany(r => r.TreeEntries)
            .HasForeignKey(e => e.RankId)
            .OnDelete(DeleteBehavior.Cascade);

        // A vehicle appears at most once per research-tree version.
        builder.HasIndex(e => new { e.ResearchTreeVersionId, e.VehicleId })
            .IsUnique();
    }
}
