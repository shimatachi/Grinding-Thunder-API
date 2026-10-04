using GrindingThunder.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GrindingThunder.Api.Infrastructure.Persistence.Configurations;

public class VehicleTypeConfiguration : IEntityTypeConfiguration<VehicleType>
{
    public void Configure(EntityTypeBuilder<VehicleType> builder)
    {
        builder.HasKey(vt => vt.Id);

        builder.Property(vt => vt.Name)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasMany(vt => vt.ResearchTrees)
            .WithOne(rt => rt.VehicleType)
            .HasForeignKey(rt => rt.VehicleTypeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
