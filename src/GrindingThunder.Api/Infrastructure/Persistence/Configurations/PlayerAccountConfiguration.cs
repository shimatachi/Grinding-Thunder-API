using GrindingThunder.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GrindingThunder.Api.Infrastructure.Persistence.Configurations;

public class PlayerAccountConfiguration : IEntityTypeConfiguration<PlayerAccount>
{
    public void Configure(EntityTypeBuilder<PlayerAccount> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Username).IsRequired().HasMaxLength(64);
        builder.Property(a => a.NormalizedUsername).IsRequired().HasMaxLength(64);
        builder.Property(a => a.Email).IsRequired().HasMaxLength(254);
        builder.Property(a => a.NormalizedEmail).IsRequired().HasMaxLength(254);
        builder.Property(a => a.PasswordHash).IsRequired().HasMaxLength(200);
        builder.HasIndex(a => a.NormalizedUsername).IsUnique();
        builder.HasIndex(a => a.NormalizedEmail).IsUnique();
        builder.Property(a => a.OptimizationMode).HasConversion<int>();
        builder.Property(a => a.ProgressStamp).IsConcurrencyToken();
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_PlayerAccounts_OptimizationMode", "\"OptimizationMode\" IN (0, 1, 2, 3)");
            t.HasCheckConstraint("CK_PlayerAccounts_AverageRpPerMatch", "\"AverageRpPerMatch\" IS NULL OR \"AverageRpPerMatch\" > 0");
            t.HasCheckConstraint("CK_PlayerAccounts_AverageNetSlPerMatch", "\"AverageNetSlPerMatch\" IS NULL OR \"AverageNetSlPerMatch\" > 0");
        });
    }
}

public class PlayerUnlockedVehicleConfiguration : IEntityTypeConfiguration<PlayerUnlockedVehicle>
{
    public void Configure(EntityTypeBuilder<PlayerUnlockedVehicle> builder)
    {
        builder.HasKey(v => new { v.PlayerAccountId, v.VehicleId });
        builder.HasOne(v => v.PlayerAccount).WithMany(a => a.UnlockedVehicles)
            .HasForeignKey(v => v.PlayerAccountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(v => v.Vehicle).WithMany()
            .HasForeignKey(v => v.VehicleId).OnDelete(DeleteBehavior.Restrict);
    }
}
