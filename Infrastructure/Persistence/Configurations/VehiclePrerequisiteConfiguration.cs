using GrindingThunder.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GrindingThunder.Api.Infrastructure.Persistence.Configurations;

public class VehiclePrerequisiteConfiguration : IEntityTypeConfiguration<VehiclePrerequisite>
{
    public void Configure(EntityTypeBuilder<VehiclePrerequisite> builder)
    {
        // Composite key: rejects duplicate edges between the same two entries.
        builder.HasKey(vp => new { vp.VehicleTreeEntryId, vp.PrerequisiteVehicleTreeEntryId });

        // SAME-VERSION INVARIANT: each FK references the composite key
        // (ResearchTreeVersionId, Id) of VehicleTreeEntries. A cross-version
        // edge (Version A entry -> Version B entry) cannot match any parent
        // row, so the database rejects it. This is the strongest practical
        // enforcement; a plain relational model cannot express it otherwise.
        builder.HasOne(vp => vp.VehicleTreeEntry)
            .WithMany(e => e.Prerequisites)
            .HasForeignKey(vp => new { vp.ResearchTreeVersionId, vp.VehicleTreeEntryId })
            .HasPrincipalKey(e => new { e.ResearchTreeVersionId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(vp => vp.PrerequisiteVehicleTreeEntry)
            .WithMany(e => e.RequiredFor)
            .HasForeignKey(vp => new { vp.ResearchTreeVersionId, vp.PrerequisiteVehicleTreeEntryId })
            .HasPrincipalKey(e => new { e.ResearchTreeVersionId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // Both sides are Cascade: deleting a version (or an entry) cascades its
        // edges. Unlike the old Vehicle-based edges (Cascade + Restrict, which
        // protected stable identities), edge data is entirely version-scoped -
        // nothing needs protecting from deletion.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_VehiclePrerequisites_NoSelfReference",
            "\"VehicleTreeEntryId\" <> \"PrerequisiteVehicleTreeEntryId\""));

        builder.HasIndex(vp => vp.ResearchTreeVersionId);
    }
}
