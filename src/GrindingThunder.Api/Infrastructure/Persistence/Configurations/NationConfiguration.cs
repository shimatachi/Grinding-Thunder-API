using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GrindingThunder.Api.Domain.Entities;

namespace GrindingThunder.Api.Infrastructure.Persistence.Configurations;

public class NationConfiguration : IEntityTypeConfiguration<Nation>
{
    public void Configure(EntityTypeBuilder<Nation> builder)
    {
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasMany(n => n.ResearchTrees)
            .WithOne(rt => rt.Nation)
            .HasForeignKey(rt => rt.NationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
