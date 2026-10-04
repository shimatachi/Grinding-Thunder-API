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

        // The entry's TreeRank is owned by the same ResearchTreeVersion as the
        // entry itself; both cascade from the version, so deleting a version
        // removes its ranks and entries together. Deleting a Vehicle never
        // cascades its entries (Restrict above) so historical placements
        // cannot vanish while a version still references them.
        builder.HasOne(e => e.TreeRank)
            .WithMany(r => r.TreeEntries)
            .HasForeignKey(e => e.TreeRankId)
            .OnDelete(DeleteBehavior.Cascade);

        // A vehicle appears at most once per research-tree version.
        builder.HasIndex(e => new { e.ResearchTreeVersionId, e.VehicleId })
            .IsUnique();
    }
}
