using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrindingThunder.Admin.Pages.Trees;

/// <summary>
/// Research-tree version detail/editor. Draft versions expose management
/// workflows for ranks, vehicle entries (costs, layout coordinates, acquisition
/// type, folder parents), and prerequisite edges. Published versions render
/// read-only from public snapshot endpoints with modification controls disabled.
/// </summary>
[Authorize]
public class VersionModel(IApiClient apiClient) : PageModel
{
    public Guid VersionId { get; private set; }
    public ResearchTreeVersionSummaryDto? Summary { get; private set; }
    public ResearchTreeDto? Tree { get; private set; }
    public DraftVersionDetailDto? Draft { get; private set; }
    public IReadOnlyList<TreeRankSummaryDto> PublishedRanks { get; private set; } = [];
    public IReadOnlyList<PublicVehicleTreeEntryDto> PublishedEntries { get; private set; } = [];
    public IReadOnlyList<VehicleDto> Vehicles { get; private set; } = [];
    public VisualResearchTreeViewModel VisualTree { get; private set; } = new([], [], [], 0);
    public VisualTreeWorkspaceViewModel VisualWorkspace => new(
        VisualTree,
        IsDraft && CanEdit && Draft is not null,
        VersionId,
        TreeLabel,
        Tree?.Id,
        Draft,
        Vehicles
            .Where(vehicle => Draft?.Entries.All(entry => entry.VehicleId != vehicle.Id) == true)
            .OrderBy(vehicle => vehicle.Name)
            .ToList());

    public bool IsDraft => Summary?.Status.Equals("Draft", StringComparison.OrdinalIgnoreCase) == true;
    public bool CanEdit => User.IsInRole("Editor") || User.IsInRole("Admin");
    public bool CanPublish => User.IsInRole("Publisher") || User.IsInRole("Admin");
    public VersionValidationDto? Validation { get; private set; }
    public string TreeLabel => Tree is null ? "Research Tree Version" : $"{Tree.NationName} — {Tree.VehicleTypeName}";

    public string? ErrorMessage { get; private set; }
    public string? NotFoundMessage { get; private set; }

    [BindProperty] public AddRankInputModel AddRank { get; set; } = new();
    [BindProperty] public AddEntryInputModel AddEntry { get; set; } = new();
    [BindProperty] public AddPrerequisiteInputModel AddPrerequisite { get; set; } = new();
    [BindProperty] public VisualEntryInputModel VisualEntry { get; set; } = new();
    [BindProperty] public CreateVehicleEntryInputModel CreateEntry { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid versionId, CancellationToken cancellationToken)
    {
        var result = await LoadAsync(versionId, cancellationToken);
        return result ?? Page();
    }

    public async Task<IActionResult> OnGetValidateAsync(Guid versionId, CancellationToken cancellationToken)
    {
        var load = await LoadAsync(versionId, cancellationToken);
        if (load is not null) return load;
        if (Summary is null) return Page();
        var response = await apiClient.ValidateResearchTreeVersionAsync(versionId, cancellationToken);
        if (!response.IsSuccess || response.Value is null)
            ErrorMessage = response.ErrorMessage ?? "Unable to validate version.";
        else
            Validation = response.Value;
        return Page();
    }

    public async Task<IActionResult> OnPostPublishAsync(Guid versionId, bool confirmImmutable, CancellationToken cancellationToken)
    {
        if (!CanPublish) return RedirectToPage("/AccessDenied");
        var load = await LoadAsync(versionId, cancellationToken);
        if (load is not null) return load;
        if (Summary is null) return Page();
        if (!IsDraft || !confirmImmutable)
        {
            ErrorMessage = IsDraft ? "Confirm that the published snapshot will be immutable." : "Only draft versions can be published.";
            return Page();
        }

        var response = await apiClient.PublishResearchTreeVersionAsync(versionId, cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess)
        {
            ErrorMessage = response.ErrorMessage ?? "Unable to publish version.";
            return Page();
        }

        TempData["SuccessMessage"] = "Version published as an immutable historical snapshot.";
        return RedirectToPage("/Trees/Version", new { versionId });
    }

    /// <summary>Resolves a versioned entry ID (draft) or vehicle ID (published) to a display name.</summary>
    public string EntryName(Guid entryOrVehicleId) =>
        Draft?.Entries.FirstOrDefault(entry => entry.Id == entryOrVehicleId)?.VehicleName
        ?? PublishedEntries.FirstOrDefault(entry => entry.Id == entryOrVehicleId)?.Name
        ?? "Unknown entry";

    public int RankNumberFor(Guid rankId) =>
        Draft?.TreeRanks.FirstOrDefault(rank => rank.Id == rankId)?.RankNumber
        ?? PublishedRanks.FirstOrDefault(rank => rank.Id == rankId)?.RankNumber
        ?? 0;

    private async Task<IActionResult?> LoadAsync(Guid versionId, CancellationToken cancellationToken)
    {
        VersionId = versionId;

        if (CanEdit)
        {
            var summary = await apiClient.GetResearchTreeVersionAsync(versionId, cancellationToken);
            if (summary.IsForbidden) return RedirectToPage("/AccessDenied");
            if (!summary.IsSuccess || summary.Value is null)
            {
                NotFoundMessage = summary.ErrorMessage ?? $"Research tree version '{versionId}' was not found.";
                return Page();
            }

            Summary = summary.Value;

            var tree = await apiClient.GetResearchTreeAsync(Summary.ResearchTreeId, cancellationToken);
            if (tree.IsForbidden) return RedirectToPage("/AccessDenied");
            Tree = tree.Value;

            if (IsDraft)
            {
                var draft = await apiClient.GetDraftOverviewAsync(versionId, cancellationToken);
                if (draft.IsSuccess && draft.Value is not null)
                {
                    Draft = draft.Value;
                }
                else
                {
                    ErrorMessage = draft.ErrorMessage ?? "Unable to load draft details.";
                }

                var vehicles = await apiClient.GetTreeVehiclesAsync(Summary.ResearchTreeId, cancellationToken);
                if (vehicles.IsSuccess) Vehicles = vehicles.Value ?? [];
            }
            else
            {
                var nations = await apiClient.GetNationsWithVersionsAsync(cancellationToken);
                if (nations.IsSuccess && nations.Value is not null)
                {
                    PublishedRanks = nations.Value
                        .SelectMany(nation => nation.ResearchTrees)
                        .Where(researchTree => researchTree.Id == Summary.ResearchTreeId)
                        .SelectMany(researchTree => researchTree.Versions)
                        .FirstOrDefault(version => version.Id == versionId)?.TreeRanks
                        ?? [];
                }

                if (Tree is not null)
                {
                    var entries = await apiClient.GetVehicleTreeAsync(Tree.NationId, Tree.VehicleTypeName, cancellationToken);
                    if (entries.IsSuccess)
                    {
                        PublishedEntries = (entries.Value ?? [])
                            .Where(entry => entry.ResearchTreeVersionId == versionId)
                            .OrderBy(entry => entry.TreeColumn)
                            .ThenBy(entry => entry.TreeRow)
                            .ToList();
                    }
                }
            }

            BuildVisualTree();
            return null;
        }

        var nationsResponse = await apiClient.GetNationsWithVersionsAsync(cancellationToken);
        if (nationsResponse.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!nationsResponse.IsSuccess || nationsResponse.Value is null)
        {
            NotFoundMessage = nationsResponse.ErrorMessage ?? $"Research tree version '{versionId}' was not found.";
            return Page();
        }

        NationVersionsDto? matchingNation = null;
        TreeWithVersionsDto? matchingTree = null;
        TreeVersionListItemDto? matchingVersion = null;

        foreach (var nation in nationsResponse.Value)
        {
            foreach (var rt in nation.ResearchTrees)
            {
                var v = rt.Versions.FirstOrDefault(ver => ver.Id == versionId);
                if (v is not null)
                {
                    matchingNation = nation;
                    matchingTree = rt;
                    matchingVersion = v;
                    break;
                }
            }
            if (matchingVersion is not null) break;
        }

        if (matchingVersion is null || matchingTree is null || matchingNation is null)
        {
            NotFoundMessage = $"Research tree version '{versionId}' was not found.";
            return Page();
        }

        Tree = new ResearchTreeDto(matchingTree.Id, matchingNation.Id, matchingNation.Name, matchingTree.VehicleTypeId, matchingTree.VehicleTypeName);
        PublishedRanks = matchingVersion.TreeRanks;

        var treeEntries = await apiClient.GetVehicleTreeAsync(matchingNation.Id, matchingTree.VehicleTypeName, cancellationToken);
        if (treeEntries.IsSuccess)
        {
            PublishedEntries = (treeEntries.Value ?? [])
                .Where(entry => entry.ResearchTreeVersionId == versionId)
                .OrderBy(entry => entry.TreeColumn)
                .ThenBy(entry => entry.TreeRow)
                .ToList();
        }

        var isPublished = matchingVersion.Status == 1;
        Summary = new ResearchTreeVersionSummaryDto(
            matchingVersion.Id,
            matchingTree.Id,
            Guid.Empty,
            matchingVersion.UpdateVersion,
            isPublished ? "Published" : "Draft",
            PublishedRanks.Count,
            PublishedEntries.Count);

        BuildVisualTree();
        return null;
    }

    private void BuildVisualTree()
    {
        VisualTree = Draft is not null
            ? VisualTreeMapper.FromDraft(Draft, Vehicles)
            : VisualTreeMapper.FromPublic(PublishedRanks, PublishedEntries);
    }

    private async Task<IActionResult?> EnsureEditableAsync(CancellationToken cancellationToken)
    {
        if (!CanEdit) return RedirectToPage("/AccessDenied");

        var result = await LoadAsync(VersionId, cancellationToken);
        if (result is not null) return result;

        if (!IsDraft)
        {
            ModelState.AddModelError(string.Empty, "Published versions are immutable and cannot be edited.");
            return Page();
        }

        return null;
    }

    private async Task<IActionResult> ExecuteDraftMutationAsync(
        Func<Task<ApiResponse>> mutation,
        string successMessage,
        string fallbackError,
        CancellationToken cancellationToken)
    {
        var guard = await EnsureEditableAsync(cancellationToken);
        if (guard is not null) return guard;

        var response = await mutation();
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, response.ErrorMessage ?? fallbackError);
            var reload = await LoadAsync(VersionId, cancellationToken);
            return reload ?? Page();
        }

        TempData["SuccessMessage"] = successMessage;
        return RedirectToPage("/Trees/Version", new { versionId = VersionId });
    }

    public Task<IActionResult> OnPostAddRankAsync(Guid versionId, CancellationToken cancellationToken)
    {
        VersionId = versionId;
        return ExecuteDraftMutationAsync(
            async () => await apiClient.AddTreeRankAsync(
                versionId,
                new AddTreeRankRequest(AddRank.RankNumber, AddRank.RequiredVehiclesUnlocked),
                cancellationToken),
            $"Rank {AddRank.RankNumber} was added.",
            "Unable to add tree rank.",
            cancellationToken);
    }

    public Task<IActionResult> OnPostUpdateRankAsync(Guid versionId, Guid rankId, int requiredVehiclesUnlocked, CancellationToken cancellationToken)
    {
        VersionId = versionId;
        return ExecuteDraftMutationAsync(
            () => apiClient.UpdateTreeRankAsync(
                versionId, rankId, new UpdateTreeRankQuotaRequest(requiredVehiclesUnlocked), cancellationToken),
            "Rank quota was updated.",
            "Unable to update tree rank.",
            cancellationToken);
    }

    public Task<IActionResult> OnPostDeleteRankAsync(Guid versionId, Guid rankId, CancellationToken cancellationToken)
    {
        VersionId = versionId;
        return ExecuteDraftMutationAsync(
            () => apiClient.DeleteTreeRankAsync(versionId, rankId, cancellationToken),
            "Tree rank was deleted.",
            "Unable to delete tree rank.",
            cancellationToken);
    }

    public async Task<IActionResult> OnPostAddEntryAsync(Guid versionId, CancellationToken cancellationToken)
    {
        VersionId = versionId;
        if (!ModelState.IsValid)
        {
            var reload = await LoadAsync(versionId, cancellationToken);
            return reload ?? Page();
        }

        return await ExecuteDraftMutationAsync(
            async () => await apiClient.AddVehicleTreeEntryAsync(
                versionId,
                new AddVehicleTreeEntryRequest(
                    AddEntry.VehicleId,
                    AddEntry.TreeRankId,
                    AddEntry.RpCost,
                    AddEntry.SlCost,
                    AddEntry.TreeColumn,
                    AddEntry.TreeRow,
                    AddEntry.AcquisitionType,
                    AddEntry.FolderParentEntryId),
                cancellationToken),
            "Vehicle tree entry was added.",
            "Unable to add vehicle tree entry.",
            cancellationToken);
    }

    public Task<IActionResult> OnPostDeleteEntryAsync(Guid versionId, Guid entryId, CancellationToken cancellationToken)
    {
        VersionId = versionId;
        return ExecuteDraftMutationAsync(
            () => apiClient.DeleteVehicleTreeEntryAsync(versionId, entryId, cancellationToken),
            "Vehicle tree entry was deleted.",
            "Unable to delete vehicle tree entry.",
            cancellationToken);
    }

    public async Task<IActionResult> OnPostUpdateVisualEntryAsync(Guid versionId, CancellationToken cancellationToken)
    {
        VersionId = versionId;
        var guard = await EnsureEditableAsync(cancellationToken);
        if (guard is not null) return guard;

        if (VisualEntry.EntryId == Guid.Empty || Draft?.Entries.All(entry => entry.Id != VisualEntry.EntryId) != false)
        {
            ModelState.AddModelError(string.Empty, "Select a vehicle entry from the visual tree before saving.");
            return Page();
        }

        if (Draft.TreeRanks.All(rank => rank.Id != VisualEntry.TreeRankId))
        {
            ModelState.AddModelError("VisualEntry.TreeRankId", "Select a rank from this draft.");
        }

        if (VisualEntry.FolderParentEntryId == VisualEntry.EntryId)
        {
            ModelState.AddModelError("VisualEntry.FolderParentEntryId", "An entry cannot be its own folder parent.");
        }
        else if (VisualEntry.FolderParentEntryId.HasValue &&
                 Draft.Entries.All(entry => entry.Id != VisualEntry.FolderParentEntryId.Value))
        {
            ModelState.AddModelError("VisualEntry.FolderParentEntryId", "Select a folder parent from this draft.");
        }

        if (!Enum.IsDefined(VisualEntry.AcquisitionType))
        {
            ModelState.AddModelError("VisualEntry.AcquisitionType", "Select a valid acquisition type.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var response = await apiClient.UpdateVehicleTreeEntryAsync(
            versionId,
            VisualEntry.EntryId,
            new UpdateVehicleTreeEntryRequest(
                VisualEntry.RpCost,
                VisualEntry.SlCost,
                VisualEntry.TreeColumn,
                VisualEntry.TreeRow,
                VisualEntry.TreeRankId,
                VisualEntry.AcquisitionType,
                VisualEntry.FolderParentEntryId),
            cancellationToken);

        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, response.ErrorMessage ?? "Unable to update vehicle tree entry.");
            return Page();
        }

        TempData["SuccessMessage"] = "Vehicle tree entry was updated from the visual editor.";
        return RedirectToPage("/Trees/Version", new { versionId });
    }

    public async Task<IActionResult> OnPostCreateAndAddEntryAsync(Guid versionId, CancellationToken cancellationToken)
    {
        VersionId = versionId;
        var guard = await EnsureEditableAsync(cancellationToken);
        if (guard is not null) return guard;

        if (string.IsNullOrWhiteSpace(CreateEntry.Name))
        {
            ModelState.AddModelError("CreateEntry.Name", "Vehicle name is required.");
        }

        if (Draft?.TreeRanks.All(rank => rank.Id != CreateEntry.TreeRankId) != false)
        {
            ModelState.AddModelError("CreateEntry.TreeRankId", "Select a rank from this draft.");
        }

        if (CreateEntry.FolderParentEntryId.HasValue &&
            Draft?.Entries.All(entry => entry.Id != CreateEntry.FolderParentEntryId.Value) != false)
        {
            ModelState.AddModelError("CreateEntry.FolderParentEntryId", "Select a folder parent from this draft.");
        }

        if (!Enum.IsDefined(CreateEntry.AcquisitionType))
        {
            ModelState.AddModelError("CreateEntry.AcquisitionType", "Select a valid acquisition type.");
        }

        if (!ModelState.IsValid || Tree is null)
        {
            return Page();
        }

        var createResponse = await apiClient.CreateVehicleAsync(
            new CreateVehicleRequest(Tree.Id, CreateEntry.Name.Trim(), CreateEntry.ImageUrl),
            cancellationToken);
        if (createResponse.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!createResponse.IsSuccess || createResponse.Value is null)
        {
            ModelState.AddModelError(string.Empty, createResponse.ErrorMessage ?? "Unable to create the vehicle.");
            return Page();
        }

        var addResponse = await apiClient.AddVehicleTreeEntryAsync(
            versionId,
            new AddVehicleTreeEntryRequest(
                createResponse.Value.Id,
                CreateEntry.TreeRankId,
                CreateEntry.RpCost,
                CreateEntry.SlCost,
                CreateEntry.TreeColumn,
                CreateEntry.TreeRow,
                CreateEntry.AcquisitionType,
                CreateEntry.FolderParentEntryId),
            cancellationToken);
        if (addResponse.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!addResponse.IsSuccess)
        {
            ModelState.AddModelError(
                string.Empty,
                $"Vehicle '{createResponse.Value.Name}' was created, but could not be placed in the draft: " +
                (addResponse.ErrorMessage ?? "Unable to add vehicle tree entry."));
            return Page();
        }

        TempData["SuccessMessage"] = $"Vehicle '{createResponse.Value.Name}' was created and added to the draft.";
        return RedirectToPage("/Trees/Version", new { versionId });
    }

    public async Task<IActionResult> OnPostAddPrerequisiteAsync(Guid versionId, CancellationToken cancellationToken)
    {
        VersionId = versionId;
        if (!ModelState.IsValid)
        {
            var reload = await LoadAsync(versionId, cancellationToken);
            return reload ?? Page();
        }

        return await ExecuteDraftMutationAsync(
            async () => await apiClient.AddVehiclePrerequisiteAsync(
                versionId,
                new AddPrerequisiteRequest(
                    AddPrerequisite.VehicleTreeEntryId,
                    AddPrerequisite.PrerequisiteVehicleTreeEntryId),
                cancellationToken),
            "Prerequisite edge was added.",
            "Unable to add prerequisite.",
            cancellationToken);
    }

    public Task<IActionResult> OnPostRemovePrerequisiteAsync(Guid versionId, Guid entryId, Guid prerequisiteEntryId, CancellationToken cancellationToken)
    {
        VersionId = versionId;
        return ExecuteDraftMutationAsync(
            () => apiClient.DeleteVehiclePrerequisiteAsync(versionId, entryId, prerequisiteEntryId, cancellationToken),
            "Prerequisite edge was removed.",
            "Unable to remove prerequisite.",
            cancellationToken);
    }
}
