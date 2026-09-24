using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GrindingThunder.Api.Domain.Entities;

namespace GrindingThunder.Api.Infrastructure.Persistence.Configurations;

public class RankConfiguration : IEntityTypeConfiguration<Rank>
{
    public void Configure(EntityTypeBuilder<Rank> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.RankNumber)
            .IsRequired();

        builder.Property(r => r.RequiredVehiclesUnlocked)
            .IsRequired();
    }
}