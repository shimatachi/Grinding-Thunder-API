using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GrindingThunder.Api.Domain.Entities;

namespace GrindingThunder.Api.Infrastructure.Persistence.Configurations;

public class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.HasOne(v => v.ResearchTree)
            .WithMany(t => t.Vehicles)
            .HasForeignKey(v => v.ResearchTreeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Database-owned normalization also protects direct SQL and concurrent writes.
        builder.Property<string>("NormalizedName")
            .HasComputedColumnSql("lower(trim(\"Name\"))", stored: true);
        builder.HasIndex("ResearchTreeId", "NormalizedName").IsUnique();
    }
}
