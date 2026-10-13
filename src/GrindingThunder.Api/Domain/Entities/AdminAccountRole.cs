namespace GrindingThunder.Api.Domain.Entities;

/// <summary>
/// One administrative role assignment for an <see cref="AdminAccount"/>.
/// The composite key prevents duplicate role assignments per account.
/// </summary>
public class AdminAccountRole
{
    public Guid AdminAccountId { get; set; }

    /// <summary>
    /// Role value; constrained at the database level to the supported
    /// Admin/Publisher/Editor set.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    public AdminAccount AdminAccount { get; set; } = null!;
}
