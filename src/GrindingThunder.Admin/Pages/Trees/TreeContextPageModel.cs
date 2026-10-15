using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrindingThunder.Admin.Pages.Trees;

// Shared route validation keeps all vehicle screens within the same nation/tree boundary.
public abstract class TreeContextPageModel(IApiClient apiClient) : PageModel
{
    protected IApiClient ApiClient { get; } = apiClient;
    public ResearchTreeDto Tree { get; private set; } = null!;
    public bool CanEdit => User.IsInRole("Editor") || IsAdmin;
    public bool IsAdmin => User.IsInRole("Admin");

    protected async Task<IActionResult?> LoadTreeAsync(Guid nationId, Guid researchTreeId, CancellationToken cancellationToken)
    {
        var response = await ApiClient.GetNationsWithVersionsAsync(cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess)
            return StatusCode((int)response.StatusCode, response.ErrorMessage ?? "Unable to load tree data.");

        var nation = response.Value?.FirstOrDefault(n => n.Id == nationId);
        var tree = nation?.ResearchTrees.FirstOrDefault(t => t.Id == researchTreeId);
        if (nation is null || tree is null) return NotFound();
        Tree = new(tree.Id, nation.Id, nation.Name, tree.VehicleTypeId, tree.VehicleTypeName);
        return null;
    }

    protected IActionResult? CheckVehicle(ApiResponse<VehicleDto> response)
    {
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess) return StatusCode((int)response.StatusCode, response.ErrorMessage ?? "Unable to load tree data.");
        if (response.Value?.ResearchTreeId != Tree.Id) return NotFound();
        return null;
    }

    protected IActionResult BackToVehicles() => RedirectToPage("/Catalog/Vehicles/Index",
        new { nationId = Tree.NationId, researchTreeId = Tree.Id });
}
