using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrindingThunder.Admin.Pages;

/// <summary>
/// Anonymous, read-only browser for Published research-tree snapshots. Selection
/// is reconstructed exclusively from the publication-filtered public API.
/// </summary>
[AllowAnonymous]
public class TreeModel(IApiClient apiClient) : PageModel
{
    public IReadOnlyList<NationVersionsDto> Nations { get; private set; } = [];
    public NationVersionsDto? SelectedNation { get; private set; }
    public IReadOnlyList<TreeWithVersionsDto> ResearchTrees { get; private set; } = [];
    public TreeWithVersionsDto? SelectedResearchTree { get; private set; }
    public IReadOnlyList<TreeVersionListItemDto> Versions { get; private set; } = [];
    public TreeVersionListItemDto? SelectedVersion { get; private set; }
    public IReadOnlyList<PublicVehicleTreeEntryDto> Entries { get; private set; } = [];
    public VisualResearchTreeViewModel VisualTree { get; private set; } = new([], [], [], 0);
    public string? ErrorMessage { get; private set; }

    [BindProperty(SupportsGet = true)] public Guid? NationId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? ResearchTreeId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? VersionId { get; set; }

    public VisualTreeWorkspaceViewModel VisualWorkspace => new(
        VisualTree,
        false,
        SelectedVersion?.Id ?? Guid.Empty,
        SelectedNation is null || SelectedResearchTree is null
            ? "Published research tree"
            : $"{SelectedNation.Name} — {SelectedResearchTree.VehicleTypeName}",
        SelectedResearchTree?.Id,
        null,
        [],
        ErrorMessage,
        false,
        false,
        null,
        null);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var catalog = await apiClient.GetPublicNationsWithVersionsAsync(cancellationToken);
        if (!catalog.IsSuccess || catalog.Value is null)
        {
            ErrorMessage = catalog.ErrorMessage ?? "Unable to load the published research-tree catalog.";
            return Page();
        }

        // Status is checked defensively even though the API contract already omits Drafts.
        Nations = catalog.Value
            .Where(nation => nation.ResearchTrees.Any(tree => tree.Versions.Any(version => version.Status == 1)))
            .OrderBy(nation => nation.Name)
            .ToList();
        if (Nations.Count == 0) return Page();

        SelectedNation = NationId.HasValue
            ? Nations.FirstOrDefault(nation => nation.Id == NationId.Value)
            : Nations[0];
        if (SelectedNation is null) return NotFound();
        NationId = SelectedNation.Id;

        ResearchTrees = SelectedNation.ResearchTrees
            .Where(tree => tree.Versions.Any(version => version.Status == 1))
            .OrderBy(tree => tree.VehicleTypeName)
            .ToList();
        SelectedResearchTree = ResearchTreeId.HasValue
            ? ResearchTrees.FirstOrDefault(tree => tree.Id == ResearchTreeId.Value)
            : ResearchTrees.FirstOrDefault();
        if (SelectedResearchTree is null) return ResearchTreeId.HasValue ? NotFound() : Page();
        ResearchTreeId = SelectedResearchTree.Id;

        // The API orders snapshots by release date/version descending, making the
        // first Published item the default current snapshot.
        Versions = SelectedResearchTree.Versions.Where(version => version.Status == 1).ToList();
        SelectedVersion = VersionId.HasValue
            ? Versions.FirstOrDefault(version => version.Id == VersionId.Value)
            : Versions.FirstOrDefault();
        if (SelectedVersion is null) return VersionId.HasValue ? NotFound() : Page();
        VersionId = SelectedVersion.Id;

        var entries = await apiClient.GetVehicleTreeAsync(
            SelectedNation.Id,
            SelectedVersion.Id,
            SelectedResearchTree.VehicleTypeName,
            cancellationToken);
        if (!entries.IsSuccess)
        {
            ErrorMessage = entries.ErrorMessage ?? "Unable to load the selected published snapshot.";
            return Page();
        }

        Entries = (entries.Value ?? [])
            .Where(entry => entry.ResearchTreeVersionId == SelectedVersion.Id)
            .OrderBy(entry => entry.TreeColumn)
            .ThenBy(entry => entry.TreeRow)
            .ToList();
        VisualTree = VisualTreeMapper.FromPublic(SelectedVersion.TreeRanks, Entries);
        return Page();
    }
}
