namespace GrindingThunder.Api.Domain.Entities;

public class ResearchTree
{
    public Guid Id { get; set; }
    public Guid NationId { get; set; }
    public Guid VehicleTypeId { get; set; }

    public Nation Nation { get; set; } = null!;
    public VehicleType VehicleType { get; set; } = null!;
    public ICollection<ResearchTreeVersion> Versions { get; set; } = new List<ResearchTreeVersion>();
}
