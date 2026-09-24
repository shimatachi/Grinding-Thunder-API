using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GrindingThunder.Api.Domain.Entities;

namespace GrindingThunder.Api.Infrastructure.Persistence.Configurations;

public class VehiclePrerequisiteConfiguration : IEntityTypeConfiguration<VehiclePrerequisite>
{
    public void Configure(EntityTypeBuilder<VehiclePrerequisite> builder)
    {
        builder.HasKey(vp => new { vp.VehicleId, vp.PrerequisiteVehicleId });

        builder.HasOne(vp => vp.Vehicle)
            .WithMany(v => v.Prerequisites)
            .HasForeignKey(vp => vp.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(vp => vp.PrerequisiteVehicle)
            .WithMany(v => v.RequiredFor)
            .HasForeignKey(vp => vp.PrerequisiteVehicleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
