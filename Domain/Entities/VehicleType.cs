namespace GrindingThunder.Api.Domain.Entities;

public class VehicleType
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<ResearchTree> ResearchTrees { get; set; } = new List<ResearchTree>();
}
