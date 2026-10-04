using Microsoft.EntityFrameworkCore;
using System.Reflection;
using GrindingThunder.Api.Domain.Entities;

namespace GrindingThunder.Api.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Nation> Nations { get; set; } = null!;
    public DbSet<VehicleType> VehicleTypes { get; set; } = null!;
    public DbSet<ResearchTree> ResearchTrees { get; set; } = null!;
    public DbSet<ResearchTreeVersion> ResearchTreeVersions { get; set; } = null!;
    public DbSet<GameUpdate> GameUpdates { get; set; } = null!;
    public DbSet<Rank> Ranks { get; set; } = null!;
    public DbSet<Vehicle> Vehicles { get; set; } = null!;
    public DbSet<VehicleTreeEntry> VehicleTreeEntries { get; set; } = null!;
    public DbSet<VehiclePrerequisite> VehiclePrerequisites { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
