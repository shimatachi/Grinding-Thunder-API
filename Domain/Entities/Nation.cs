namespace GrindingThunder.Api.Domain.Entities;

public class Nation
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;

    public ICollection<Rank> Ranks { get; set; } = new List<Rank>();
}
