using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrindingThunder.Admin.Pages.Trees;

[Authorize]
public class DiffModel(IApiClient apiClient) : PageModel
{
    public Guid? NationId { get; private set; }
    public Guid ResearchTreeId { get; private set; }
    public Guid SourceVersionId { get; private set; }
    public Guid TargetVersionId { get; private set; }
    public string TreeLabel { get; private set; } = "Research tree";
    public IReadOnlyList<TreeVersionListItemDto> Versions { get; private set; } = [];
    public VersionDiffDto? Diff { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(Guid researchTreeId, Guid? sourceVersionId, Guid? targetVersionId, CancellationToken cancellationToken)
    {
        ResearchTreeId = researchTreeId;
        var response = await apiClient.GetNationsWithVersionsAsync(cancellationToken);
        if (!response.IsSuccess || response.Value is null)
        {
            ErrorMessage = response.ErrorMessage ?? "Unable to list versions.";
            return;
        }

        var match = response.Value.SelectMany(nation => nation.ResearchTrees.Select(tree => (nation, tree)))
            .FirstOrDefault(item => item.tree.Id == researchTreeId);
        if (match.tree is null)
        {
            ErrorMessage = "Research tree was not found.";
            return;
        }

        TreeLabel = $"{match.nation.Name} / {match.tree.VehicleTypeName}";
        NationId = match.nation.Id;
        Versions = match.tree.Versions.OrderByDescending(version => version.UpdateVersion, StringComparer.OrdinalIgnoreCase).ToList();
        SourceVersionId = sourceVersionId ?? Guid.Empty;
        TargetVersionId = targetVersionId ?? Guid.Empty;
        if (SourceVersionId == Guid.Empty || TargetVersionId == Guid.Empty) return;
        if (SourceVersionId == TargetVersionId || !Versions.Any(version => version.Id == SourceVersionId) ||
            !Versions.Any(version => version.Id == TargetVersionId))
        {
            ErrorMessage = "Choose two different versions from the same research tree.";
            return;
        }

        var diff = await apiClient.CompareResearchTreeVersionsAsync(SourceVersionId, TargetVersionId, cancellationToken);
        if (!diff.IsSuccess || diff.Value is null)
            ErrorMessage = diff.ErrorMessage ?? "Unable to compare versions.";
        else
            Diff = diff.Value;
    }
}
