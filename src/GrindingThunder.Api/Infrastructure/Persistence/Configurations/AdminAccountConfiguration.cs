using GrindingThunder.Api.Application.Security;
using GrindingThunder.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GrindingThunder.Api.Infrastructure.Persistence.Configurations;

public class AdminAccountConfiguration : IEntityTypeConfiguration<AdminAccount>
{
    public void Configure(EntityTypeBuilder<AdminAccount> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Username)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(a => a.NormalizedUsername)
            .IsRequired()
            .HasMaxLength(64);

        // Case-insensitive username uniqueness is enforced by the database,
        // not only by application checks.
        builder.HasIndex(a => a.NormalizedUsername)
            .IsUnique();

        builder.Property(a => a.PasswordHash)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.IsActive)
            .IsRequired();

        builder.Property(a => a.SecurityStamp)
            .IsRequired();

        builder.HasMany(a => a.Roles)
            .WithOne(r => r.AdminAccount)
            .HasForeignKey(r => r.AdminAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AdminAccountRoleConfiguration : IEntityTypeConfiguration<AdminAccountRole>
{
    public void Configure(EntityTypeBuilder<AdminAccountRole> builder)
    {
        builder.HasKey(r => new { r.AdminAccountId, r.Role });

        builder.Property(r => r.Role)
            .IsRequired()
            .HasMaxLength(32);

        // Only the supported administrative roles may be persisted.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AdminAccountRoles_SupportedRole",
            $"\"Role\" IN ('{AuthRoles.Admin}', '{AuthRoles.Publisher}', '{AuthRoles.Editor}')"));
    }
}
