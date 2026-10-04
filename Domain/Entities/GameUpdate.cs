namespace GrindingThunder.Api.Domain.Entities;

/// <summary>
/// A War Thunder game update. One update may contain versions of many
/// research trees. Independent from any nation or tree.
/// </summary>
public class GameUpdate
{
    public Guid Id { get; set; }
    public string Version { get; set; } = string.Empty;
    public string? Name { get; set; }
    public DateOnly? ReleaseDate { get; set; }

    public ICollection<ResearchTreeVersion> ResearchTreeVersions { get; set; }
        = new List<ResearchTreeVersion>();
}
