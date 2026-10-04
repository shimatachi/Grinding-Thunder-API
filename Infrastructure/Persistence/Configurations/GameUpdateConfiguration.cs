using GrindingThunder.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GrindingThunder.Api.Infrastructure.Persistence.Configurations;

public class GameUpdateConfiguration : IEntityTypeConfiguration<GameUpdate>
{
    public void Configure(EntityTypeBuilder<GameUpdate> builder)
    {
        builder.HasKey(gu => gu.Id);

        builder.Property(gu => gu.Version)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(gu => gu.Name)
            .HasMaxLength(100);

        // The version value identifies an update; two updates must not
        // share the same version string.
        builder.HasIndex(gu => gu.Version)
            .IsUnique();

        builder.HasMany(gu => gu.ResearchTreeVersions)
            .WithOne(rtv => rtv.GameUpdate)
            .HasForeignKey(rtv => rtv.GameUpdateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
