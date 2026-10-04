using GrindingThunder.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GrindingThunder.Api.Infrastructure.Persistence.Configurations;

public class ResearchTreeConfiguration : IEntityTypeConfiguration<ResearchTree>
{
    public void Configure(EntityTypeBuilder<ResearchTree> builder)
    {
        builder.HasKey(rt => rt.Id);

        builder.HasOne(rt => rt.Nation)
            .WithMany(n => n.ResearchTrees)
            .HasForeignKey(rt => rt.NationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(rt => rt.VehicleType)
            .WithMany(vt => vt.ResearchTrees)
            .HasForeignKey(rt => rt.VehicleTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(rt => rt.Ranks)
            .WithOne(r => r.ResearchTree)
            .HasForeignKey(r => r.ResearchTreeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(rt => new { rt.NationId, rt.VehicleTypeId })
            .IsUnique();
    }
}
