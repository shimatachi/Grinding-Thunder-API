using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrindingThunder.Admin.Pages.Catalog.GameUpdates;

[Authorize]
public class WorkspaceModel(IApiClient apiClient) : PageModel
{
    public GameUpdateDto? GameUpdate { get; private set; }
    public IReadOnlyList<NationReleaseGroup> Nations { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public bool CanEdit => User.IsInRole("Editor") || User.IsInRole("Admin");
    public bool CanPublish => User.IsInRole("Publisher") || User.IsInRole("Admin");
    public int TotalTrees => Nations.Sum(nation => nation.Trees.Count);
    public int PublishedCount => Nations.Sum(nation => nation.Trees.Count(tree => tree.IsPublished));
    public int ValidDraftCount => Nations.Sum(nation => nation.Trees.Count(tree => tree.IsDraft && tree.Validation?.IsValid == true));
    public int DraftWithErrorsCount => Nations.Sum(nation => nation.Trees.Count(tree => tree.IsDraft && tree.Validation?.IsValid == false));
    public int MissingCount => Nations.Sum(nation => nation.Trees.Count(tree => tree.Version is null));

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await LoadAsync(id, true, cancellationToken);
        return result ?? Page();
    }

    public async Task<IActionResult> OnPostCreateDraftAsync(Guid id, Guid researchTreeId, CancellationToken cancellationToken)
    {
        if (!CanEdit) return RedirectToPage("/AccessDenied");
        var load = await LoadAsync(id, false, cancellationToken);
        if (load is not null) return load;
        var tree = FindTree(researchTreeId);
        if (tree is null || tree.Version is not null) return NotFound();
        var response = await apiClient.CreateDraftVersionAsync(new(researchTreeId, id), cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess)
            TempData["WorkspaceErrorMessage"] = response.ErrorMessage ?? "Unable to create draft version.";
        else
            TempData["SuccessMessage"] = $"Blank draft created for {tree.NationName} / {tree.VehicleTypeName}.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCloneAsync(Guid id, Guid researchTreeId, Guid sourceVersionId, CancellationToken cancellationToken)
    {
        if (!CanEdit) return RedirectToPage("/AccessDenied");
        var load = await LoadAsync(id, false, cancellationToken);
        if (load is not null) return load;
        var tree = FindTree(researchTreeId);
        if (tree is null || tree.Version is not null || tree.CloneSource?.Id != sourceVersionId) return NotFound();
        var response = await apiClient.CloneResearchTreeVersionAsync(sourceVersionId, new(id), cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess)
            TempData["WorkspaceErrorMessage"] = response.ErrorMessage ?? "Unable to clone the published version.";
        else
            TempData["SuccessMessage"] = $"Published {tree.CloneSource.UpdateVersion} snapshot cloned for {tree.NationName} / {tree.VehicleTypeName}.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostPublishAsync(Guid id, Guid researchTreeId, Guid versionId, bool confirmImmutable, CancellationToken cancellationToken)
    {
        if (!CanPublish) return RedirectToPage("/AccessDenied");
        var load = await LoadAsync(id, false, cancellationToken);
        if (load is not null) return load;
        var tree = FindTree(researchTreeId);
        if (tree is null || !tree.IsDraft || tree.Version?.Id != versionId) return NotFound();
        if (!confirmImmutable)
        {
            TempData["WorkspaceErrorMessage"] = "Confirm that the published snapshot will be immutable.";
            return RedirectToPage(new { id });
        }
        var response = await apiClient.PublishResearchTreeVersionAsync(versionId, cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess)
            TempData["WorkspaceErrorMessage"] = response.ErrorMessage ?? "Unable to publish version.";
        else
            TempData["SuccessMessage"] = $"{tree.NationName} / {tree.VehicleTypeName} was published as an immutable snapshot.";
        return RedirectToPage(new { id });
    }

    private ReleaseTreeItem? FindTree(Guid researchTreeId) =>
        Nations.SelectMany(nation => nation.Trees).FirstOrDefault(tree => tree.ResearchTreeId == researchTreeId);

    private async Task<IActionResult?> LoadAsync(Guid id, bool validateDrafts, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return NotFound();
        var updateResponse = await apiClient.GetGameUpdateAsync(id, cancellationToken);
        if (updateResponse.IsForbidden) return RedirectToPage("/AccessDenied");
        if (updateResponse.IsNotFound || updateResponse.Value is null) return NotFound();
        if (!updateResponse.IsSuccess)
        {
            ErrorMessage = updateResponse.ErrorMessage ?? "Unable to load game update.";
            return null;
        }
        GameUpdate = updateResponse.Value;
        var nationsResponse = await apiClient.GetNationsWithVersionsAsync(cancellationToken);
        if (!nationsResponse.IsSuccess || nationsResponse.Value is null)
        {
            ErrorMessage = nationsResponse.ErrorMessage ?? "Unable to load research trees.";
            return null;
        }

        var groups = new List<NationReleaseGroup>();
        foreach (var nation in nationsResponse.Value.OrderBy(nation => nation.Name))
        {
            var trees = new List<ReleaseTreeItem>();
            foreach (var tree in nation.ResearchTrees.OrderBy(tree => tree.VehicleTypeName))
            {
                var target = tree.Versions.SingleOrDefault(version => version.GameUpdateId == id);
                var cloneSource = tree.Versions.Where(version => version.Status == 1 && version.GameUpdateId != id)
                    .OrderByDescending(version => version.UpdateVersion, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                VersionValidationDto? validation = null;
                string? validationError = null;
                if (validateDrafts && target?.Status == 0)
                {
                    var response = await apiClient.ValidateResearchTreeVersionAsync(target.Id, cancellationToken);
                    if (response.IsSuccess && response.Value is not null) validation = response.Value;
                    else validationError = response.ErrorMessage ?? "Validation could not be completed.";
                }
                trees.Add(new(nation.Id, nation.Name, tree.Id, tree.VehicleTypeName, target, cloneSource, validation, validationError));
            }
            groups.Add(new(nation.Id, nation.Name, trees));
        }
        Nations = groups;
        return null;
    }

    public record NationReleaseGroup(Guid NationId, string Name, IReadOnlyList<ReleaseTreeItem> Trees);
    public record ReleaseTreeItem(Guid NationId, string NationName, Guid ResearchTreeId, string VehicleTypeName,
        TreeVersionListItemDto? Version, TreeVersionListItemDto? CloneSource, VersionValidationDto? Validation, string? ValidationError)
    {
        public bool IsDraft => Version?.Status == 0;
        public bool IsPublished => Version?.Status == 1;
    }
}
