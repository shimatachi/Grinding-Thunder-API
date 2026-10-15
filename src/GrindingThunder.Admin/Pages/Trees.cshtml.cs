using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrindingThunder.Admin.Pages;

/// <summary>
/// Nation-first browsing, category setup, and the selected tree's version workspace.
/// </summary>
[Authorize]
public class TreesModel(IApiClient apiClient) : PageModel
{
    public IReadOnlyList<NationVersionsDto> Nations { get; private set; } = [];
    public NationVersionsDto? SelectedNation { get; private set; }
    public IReadOnlyList<VehicleTypeDto> AvailableCategories { get; private set; } = [];
    public IReadOnlyList<ResearchTreeDto> Trees { get; private set; } = [];
    public IReadOnlyList<GameUpdateDto> GameUpdates { get; private set; } = [];
    public Guid SelectedTreeId { get; private set; }
    public ResearchTreeDto? SelectedTree { get; private set; }
    public IReadOnlyList<VersionListItem> Versions { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    public bool CanEdit => User.IsInRole("Editor") || IsAdmin;
    public bool IsAdmin => User.IsInRole("Admin");

    public record VersionListItem(Guid Id, string UpdateVersion, bool IsPublished, int RankCount);

    public async Task<IActionResult> OnGetAsync(Guid? nationId, Guid? researchTreeId, CancellationToken cancellationToken)
    {
        var response = await apiClient.GetNationsWithVersionsAsync(cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess)
        {
            ErrorMessage = response.ErrorMessage;
            return Page();
        }

        Nations = (response.Value ?? []).OrderBy(n => n.Name).ToList();
        Trees = Nations.SelectMany(n => n.ResearchTrees.Select(t =>
            new ResearchTreeDto(t.Id, n.Id, n.Name, t.VehicleTypeId, t.VehicleTypeName))).ToList();
        // Old bookmarks with only researchTreeId still open that tree's versions.
        if (researchTreeId.HasValue)
        {
            SelectedTree = Trees.FirstOrDefault(t => t.Id == researchTreeId && (!nationId.HasValue || t.NationId == nationId));
            if (SelectedTree is null) return NotFound();
            SelectedTreeId = SelectedTree.Id;
            nationId = SelectedTree.NationId;
        }
        if (!nationId.HasValue) return Page();
        SelectedNation = Nations.FirstOrDefault(n => n.Id == nationId);
        if (SelectedNation is null) return NotFound();

        if (SelectedTree is null)
        {
            if (CanEdit)
            {
                var categories = await apiClient.GetVehicleTypesAsync(cancellationToken);
                if (categories.IsForbidden) return RedirectToPage("/AccessDenied");
                if (!categories.IsSuccess) ErrorMessage = categories.ErrorMessage;
                AvailableCategories = (categories.Value ?? [])
                    .Where(c => !SelectedNation.ResearchTrees.Any(t => t.VehicleTypeId == c.Id))
                    .OrderBy(c => c.Name).ToList();
            }
            return Page();
        }

        Versions = SelectedNation.ResearchTrees.Single(t => t.Id == SelectedTreeId).Versions
            .OrderBy(v => v.Status).ThenByDescending(v => v.UpdateVersion, StringComparer.OrdinalIgnoreCase)
            .Select(v => new VersionListItem(v.Id, v.UpdateVersion, v.Status == 1, v.TreeRanks.Count)).ToList();
        if (CanEdit)
        {
            var updates = await apiClient.GetGameUpdatesAsync(cancellationToken);
            if (updates.IsForbidden) return RedirectToPage("/AccessDenied");
            if (updates.IsSuccess) GameUpdates = updates.Value ?? [];
            else ErrorMessage = updates.ErrorMessage;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostEnableCategoryAsync([FromRoute] Guid nationId, Guid vehicleTypeId, CancellationToken cancellationToken)
    {
        if (!CanEdit) return RedirectToPage("/AccessDenied");
        var page = await OnGetAsync(nationId, null, cancellationToken);
        if (page is not PageResult || ErrorMessage is not null) return page;
        if (!ModelState.IsValid || !AvailableCategories.Any(c => c.Id == vehicleTypeId))
        {
            ModelState.AddModelError(string.Empty, "Choose an available category for this nation.");
            return Page();
        }
        var response = await apiClient.CreateResearchTreeAsync(new(nationId, vehicleTypeId), cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, response.ErrorMessage ?? "Unable to enable category.");
            return Page();
        }
        TempData["SuccessMessage"] = "Category enabled for this nation.";
        return RedirectToPage("/Trees", new { nationId, researchTreeId = (Guid?)null });
    }

    public async Task<IActionResult> OnPostCreateDraftAsync(Guid researchTreeId, Guid gameUpdateId, CancellationToken cancellationToken)
    {
        if (!CanEdit) return RedirectToPage("/AccessDenied");
        var contextError = await CheckWorkspaceAsync(researchTreeId, null, cancellationToken);
        if (contextError is not null) return contextError;

        var response = await apiClient.CreateDraftVersionAsync(
            new CreateDraftVersionRequest(researchTreeId, gameUpdateId), cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess || response.Value is null)
        {
            TempData["TreeErrorMessage"] = response.ErrorMessage ?? "Unable to create draft version.";
            return BackToVersions();
        }

        return RedirectToPage("/Trees/Version", new { versionId = response.Value.Id });
    }

    public async Task<IActionResult> OnPostCloneAsync(Guid versionId, Guid targetGameUpdateId, Guid researchTreeId, CancellationToken cancellationToken)
    {
        if (!CanEdit) return RedirectToPage("/AccessDenied");
        var contextError = await CheckWorkspaceAsync(researchTreeId, versionId, cancellationToken);
        if (contextError is not null) return contextError;

        var response = await apiClient.CloneResearchTreeVersionAsync(
            versionId, new CloneVersionRequest(targetGameUpdateId), cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess || response.Value is null)
        {
            TempData["TreeErrorMessage"] = response.ErrorMessage ?? "Unable to clone version.";
            return BackToVersions();
        }

        return RedirectToPage("/Trees/Version", new { versionId = response.Value.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid versionId, Guid researchTreeId, CancellationToken cancellationToken)
    {
        if (!IsAdmin) return RedirectToPage("/AccessDenied");
        var contextError = await CheckWorkspaceAsync(researchTreeId, versionId, cancellationToken);
        if (contextError is not null) return contextError;

        var response = await apiClient.DeleteResearchTreeVersionAsync(versionId, cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess)
        {
            TempData["TreeErrorMessage"] = response.ErrorMessage ?? "Unable to delete draft version.";
            return BackToVersions();
        }

        TempData["SuccessMessage"] = "Draft version was deleted.";
        return BackToVersions();
    }

    private IActionResult BackToVersions() => RedirectToPage("/Trees",
        new { nationId = SelectedTree!.NationId, researchTreeId = SelectedTree.Id });

    private async Task<IActionResult?> CheckWorkspaceAsync(Guid researchTreeId, Guid? versionId, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest();
        // Never let form fields move a contextual operation to a different tree.
        if (Guid.TryParse(RouteData.Values["researchTreeId"]?.ToString(), out var routeTree) && routeTree != researchTreeId)
            return NotFound();
        Guid? nationId = Guid.TryParse(RouteData.Values["nationId"]?.ToString(), out var routeNation) ? routeNation : null;
        var response = await apiClient.GetNationsWithVersionsAsync(cancellationToken);
        if (!response.IsSuccess) return StatusCode((int)response.StatusCode, response.ErrorMessage ?? "Unable to load research trees.");
        var nation = response.Value?.FirstOrDefault(n => (!nationId.HasValue || n.Id == nationId) && n.ResearchTrees.Any(t => t.Id == researchTreeId));
        var tree = nation?.ResearchTrees.FirstOrDefault(t => t.Id == researchTreeId);
        if (nation is null || tree is null || (versionId.HasValue && !tree.Versions.Any(v => v.Id == versionId))) return NotFound();
        SelectedTree = new(tree.Id, nation.Id, nation.Name, tree.VehicleTypeId, tree.VehicleTypeName);
        return null;
    }
}

