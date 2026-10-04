using GrindingThunder.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GrindingThunder.Api.Infrastructure.Persistence.Configurations;

public class ResearchTreeVersionConfiguration : IEntityTypeConfiguration<ResearchTreeVersion>
{
    public void Configure(EntityTypeBuilder<ResearchTreeVersion> builder)
    {
        builder.HasKey(rtv => rtv.Id);

        builder.Property(rtv => rtv.Status)
            .HasConversion<int>();

        builder.HasOne(rtv => rtv.ResearchTree)
            .WithMany(rt => rt.Versions)
            .HasForeignKey(rtv => rtv.ResearchTreeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(rtv => rtv.GameUpdate)
            .WithMany(gu => gu.ResearchTreeVersions)
            .HasForeignKey(rtv => rtv.GameUpdateId)
            .OnDelete(DeleteBehavior.Cascade);

        // One research tree must not have two versions of the same game update.
        // A later explicit revision model (if ever needed) would change this rule.
        builder.HasIndex(rtv => new { rtv.ResearchTreeId, rtv.GameUpdateId })
            .IsUnique();
    }
}
