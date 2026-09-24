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

        builder.Property(n => n.Type)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasMany(n => n.Ranks)
            .WithOne(r => r.Nation)
            .HasForeignKey(r => r.NationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}