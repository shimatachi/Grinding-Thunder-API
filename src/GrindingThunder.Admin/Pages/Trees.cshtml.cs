using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrindingThunder.Admin.Pages;

/// <summary>
/// Research-tree version index: selects a research tree and lists its draft and
/// published version snapshots with lifecycle controls (create draft, clone,
/// delete draft).
/// </summary>
[Authorize]
public class TreesModel(IApiClient apiClient) : PageModel
{
    public IReadOnlyList<ResearchTreeDto> Trees { get; private set; } = [];
    public IReadOnlyList<GameUpdateDto> GameUpdates { get; private set; } = [];
    public Guid SelectedTreeId { get; private set; }
    public ResearchTreeDto? SelectedTree { get; private set; }
    public IReadOnlyList<VersionListItem> Versions { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    public bool CanEdit => User.IsInRole("Editor") || IsAdmin;
    public bool IsAdmin => User.IsInRole("Admin");

    public record VersionListItem(Guid Id, string UpdateVersion, bool IsPublished, int RankCount);

    public async Task<IActionResult> OnGetAsync(Guid? researchTreeId, CancellationToken cancellationToken)
    {
        if (CanEdit)
        {
            var trees = await apiClient.GetResearchTreesAsync(cancellationToken);
            if (trees.IsForbidden) return RedirectToPage("/AccessDenied");
            if (trees.IsSuccess) Trees = trees.Value ?? [];
            else ErrorMessage = trees.ErrorMessage;

            var updates = await apiClient.GetGameUpdatesAsync(cancellationToken);
            if (updates.IsSuccess) GameUpdates = updates.Value ?? [];

            SelectedTreeId = researchTreeId ?? Trees.FirstOrDefault()?.Id ?? Guid.Empty;
            SelectedTree = Trees.FirstOrDefault(tree => tree.Id == SelectedTreeId);

            if (SelectedTree is not null)
            {
                var nations = await apiClient.GetNationsWithVersionsAsync(cancellationToken);
                if (nations.IsSuccess && nations.Value is not null)
                {
                    Versions = nations.Value
                        .SelectMany(nation => nation.ResearchTrees)
                        .Where(tree => tree.Id == SelectedTreeId)
                        .SelectMany(tree => tree.Versions)
                        .OrderBy(version => version.Status)
                        .ThenByDescending(version => version.UpdateVersion, StringComparer.OrdinalIgnoreCase)
                        .Select(version => new VersionListItem(
                            version.Id,
                            version.UpdateVersion,
                            version.Status == 1,
                            version.TreeRanks.Count))
                        .ToList();
                }
                else if (!nations.IsSuccess)
                {
                    ErrorMessage = nations.ErrorMessage;
                }
            }

            return Page();
        }
        else
        {
            var nations = await apiClient.GetNationsWithVersionsAsync(cancellationToken);
            if (nations.IsForbidden) return RedirectToPage("/AccessDenied");
            if (!nations.IsSuccess || nations.Value is null)
            {
                ErrorMessage = nations.ErrorMessage;
                return Page();
            }

            Trees = nations.Value
                .SelectMany(nation => nation.ResearchTrees.Select(tree => new ResearchTreeDto(
                    tree.Id,
                    nation.Id,
                    nation.Name,
                    tree.VehicleTypeId,
                    tree.VehicleTypeName)))
                .OrderBy(tree => tree.NationName)
                .ThenBy(tree => tree.VehicleTypeName)
                .ToList();

            SelectedTreeId = researchTreeId ?? Trees.FirstOrDefault()?.Id ?? Guid.Empty;
            SelectedTree = Trees.FirstOrDefault(tree => tree.Id == SelectedTreeId);

            if (SelectedTree is not null)
            {
                Versions = nations.Value
                    .SelectMany(nation => nation.ResearchTrees)
                    .Where(tree => tree.Id == SelectedTreeId)
                    .SelectMany(tree => tree.Versions)
                    .OrderBy(version => version.Status)
                    .ThenByDescending(version => version.UpdateVersion, StringComparer.OrdinalIgnoreCase)
                    .Select(version => new VersionListItem(
                        version.Id,
                        version.UpdateVersion,
                        version.Status == 1,
                        version.TreeRanks.Count))
                    .ToList();
            }

            return Page();
        }
    }

    public async Task<IActionResult> OnPostCreateDraftAsync(Guid researchTreeId, Guid gameUpdateId, CancellationToken cancellationToken)
    {
        if (!CanEdit) return RedirectToPage("/AccessDenied");

        var response = await apiClient.CreateDraftVersionAsync(
            new CreateDraftVersionRequest(researchTreeId, gameUpdateId), cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess || response.Value is null)
        {
            TempData["TreeErrorMessage"] = response.ErrorMessage ?? "Unable to create draft version.";
            return RedirectToPage("/Trees", new { researchTreeId });
        }

        return RedirectToPage("/Trees/Version", new { versionId = response.Value.Id });
    }

    public async Task<IActionResult> OnPostCloneAsync(Guid versionId, Guid targetGameUpdateId, Guid researchTreeId, CancellationToken cancellationToken)
    {
        if (!CanEdit) return RedirectToPage("/AccessDenied");

        var response = await apiClient.CloneResearchTreeVersionAsync(
            versionId, new CloneVersionRequest(targetGameUpdateId), cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess || response.Value is null)
        {
            TempData["TreeErrorMessage"] = response.ErrorMessage ?? "Unable to clone version.";
            return RedirectToPage("/Trees", new { researchTreeId });
        }

        return RedirectToPage("/Trees/Version", new { versionId = response.Value.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid versionId, Guid researchTreeId, CancellationToken cancellationToken)
    {
        if (!IsAdmin) return RedirectToPage("/AccessDenied");

        var response = await apiClient.DeleteResearchTreeVersionAsync(versionId, cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess)
        {
            TempData["TreeErrorMessage"] = response.ErrorMessage ?? "Unable to delete draft version.";
            return RedirectToPage("/Trees", new { researchTreeId });
        }

        TempData["SuccessMessage"] = "Draft version was deleted.";
        return RedirectToPage("/Trees", new { researchTreeId });
    }
}

