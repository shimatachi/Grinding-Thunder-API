using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GrindingThunder.Api.Domain.Entities;

namespace GrindingThunder.Api.Infrastructure.Persistence.Configurations;

public class TreeRankConfiguration : IEntityTypeConfiguration<TreeRank>
{
    public void Configure(EntityTypeBuilder<TreeRank> builder)
    {
        builder.HasKey(tr => tr.Id);

        builder.Property(tr => tr.RankNumber)
            .IsRequired();

        builder.Property(tr => tr.RequiredVehiclesUnlocked)
            .IsRequired();

        // TreeRank is owned by a version: deleting a version cascades its ranks.
        builder.HasOne(tr => tr.ResearchTreeVersion)
            .WithMany(rtv => rtv.TreeRanks)
            .HasForeignKey(tr => tr.ResearchTreeVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        // A rank number is unique within one version; the same number may
        // exist in different versions (independent configurations).
        builder.HasIndex(tr => new { tr.ResearchTreeVersionId, tr.RankNumber })
            .IsUnique();
    }
}
