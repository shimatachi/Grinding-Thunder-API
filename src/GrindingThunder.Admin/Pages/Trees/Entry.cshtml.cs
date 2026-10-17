using System.Net;
using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrindingThunder.Admin.Pages.Trees;

/// <summary>
/// Full edit form for a vehicle tree entry inside a draft version: rank,
/// RP/SL costs, visual TreeColumn/TreeRow coordinates, acquisition type,
/// and folder parent grouping. Published versions are rejected up front
/// because the draft overview endpoint returns 409 Conflict for them.
/// </summary>
[Authorize(Roles = "Editor,Admin")]
public class EntryModel(IApiClient apiClient) : PageModel
{
    public Guid VersionId { get; private set; }
    public Guid EntryId { get; private set; }
    public string? VehicleName { get; private set; }
    public IReadOnlyList<TreeRankDto> Ranks { get; private set; } = [];
    public IReadOnlyList<VehicleTreeEntryDto> OtherEntries { get; private set; } = [];

    public string? NotFoundMessage { get; private set; }
    public string? ImmutableMessage { get; private set; }

    [BindProperty] public EntryInputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid versionId, Guid entryId, CancellationToken cancellationToken)
    {
        VersionId = versionId;
        EntryId = entryId;

        var overview = await apiClient.GetDraftOverviewAsync(versionId, cancellationToken);
        if (overview.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!overview.IsSuccess || overview.Value is null)
        {
            if (overview.StatusCode == HttpStatusCode.Conflict)
            {
                ImmutableMessage = overview.ErrorMessage ?? "Published versions are immutable.";
                return Page();
            }

            NotFoundMessage = overview.ErrorMessage ?? "Draft version was not found.";
            return Page();
        }

        var draft = overview.Value;
        var entry = draft.Entries.FirstOrDefault(entry => entry.Id == entryId);
        if (entry is null)
        {
            NotFoundMessage = $"Vehicle tree entry '{entryId}' was not found in this draft version.";
            return Page();
        }

        LoadSelections(draft, entryId);
        VehicleName = entry.VehicleName;
        Input = new EntryInputModel
        {
            TreeRankId = entry.TreeRankId,
            RpCost = entry.RpCost,
            SlCost = entry.SlCost,
            TreeColumn = entry.TreeColumn + 1,
            TreeRow = entry.TreeRow + 1,
            AcquisitionType = entry.AcquisitionType,
            FolderParentEntryId = entry.FolderParentEntryId
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid versionId, Guid entryId, CancellationToken cancellationToken)
    {
        VersionId = versionId;
        EntryId = entryId;

        var overview = await apiClient.GetDraftOverviewAsync(versionId, cancellationToken);
        if (overview.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!overview.IsSuccess || overview.Value is null)
        {
            if (overview.StatusCode == HttpStatusCode.Conflict)
                ImmutableMessage = overview.ErrorMessage ?? "Published versions are immutable.";
            else
                NotFoundMessage = overview.ErrorMessage ?? "Draft version was not found.";
            return Page();
        }

        var draft = overview.Value;
        var entry = draft.Entries.FirstOrDefault(entry => entry.Id == entryId);
        if (entry is null)
        {
            NotFoundMessage = $"Vehicle tree entry '{entryId}' was not found in this draft version.";
            return Page();
        }

        LoadSelections(draft, entryId);
        VehicleName = entry.VehicleName;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var response = await apiClient.UpdateVehicleTreeEntryAsync(
            versionId,
            entryId,
            new UpdateVehicleTreeEntryRequest(
                Input.RpCost,
                Input.SlCost,
                Input.TreeColumn - 1,
                Input.TreeRow - 1,
                Input.TreeRankId,
                Input.AcquisitionType,
                Input.FolderParentEntryId),
            cancellationToken);

        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, response.ErrorMessage ?? "Unable to update vehicle tree entry.");
            await ReloadSelectionsAsync(cancellationToken);
            return Page();
        }

        TempData["SuccessMessage"] = "Vehicle tree entry was updated.";
        return RedirectToPage("/Trees/Version", new { versionId });
    }

    private void LoadSelections(DraftVersionDetailDto draft, Guid entryId)
    {
        Ranks = [.. draft.TreeRanks.OrderBy(rank => rank.RankNumber)];
        OtherEntries = [.. draft.Entries.Where(entry => entry.Id != entryId).OrderBy(entry => entry.VehicleName)];
    }

    private async Task ReloadSelectionsAsync(CancellationToken cancellationToken)
    {
        var overview = await apiClient.GetDraftOverviewAsync(VersionId, cancellationToken);
        if (overview.IsSuccess && overview.Value is not null)
        {
            var draft = overview.Value;
            LoadSelections(draft, EntryId);
            VehicleName = draft.Entries.FirstOrDefault(entry => entry.Id == EntryId)?.VehicleName;
        }
    }
}
