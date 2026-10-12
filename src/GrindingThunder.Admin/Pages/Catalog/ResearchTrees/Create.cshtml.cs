using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace GrindingThunder.Admin.Pages.Catalog.ResearchTrees;
[Authorize]
public class CreateModel(IApiClient apiClient) : PageModel
{
    [BindProperty] public ResearchTreeInputModel Input { get; set; } = new();
    public IReadOnlyList<NationDto> Nations { get; private set; } = [];
    public IReadOnlyList<VehicleTypeDto> VehicleTypes { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) => await LoadOptionsAsync(cancellationToken);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (Input.NationId == Guid.Empty) ModelState.AddModelError("Input.NationId", "Nation is required.");
        if (Input.VehicleTypeId == Guid.Empty) ModelState.AddModelError("Input.VehicleTypeId", "Vehicle type is required.");
        if (!ModelState.IsValid) return await LoadOptionsAsync(cancellationToken);

        var response = await apiClient.CreateResearchTreeAsync(new CreateResearchTreeRequest(Input.NationId, Input.VehicleTypeId), cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess) { ModelState.AddModelError(string.Empty, response.ErrorMessage ?? "Unable to create research tree."); return await LoadOptionsAsync(cancellationToken); }
        TempData["SuccessMessage"] = "Research tree was created."; return RedirectToPage("Index");
    }

    private async Task<IActionResult> LoadOptionsAsync(CancellationToken cancellationToken)
    {
        var nations = await apiClient.GetNationsAsync(cancellationToken);
        var vehicleTypes = await apiClient.GetVehicleTypesAsync(cancellationToken);
        if (nations.IsForbidden || vehicleTypes.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!nations.IsSuccess) ModelState.AddModelError(string.Empty, nations.ErrorMessage ?? "Unable to load nations.");
        if (!vehicleTypes.IsSuccess) ModelState.AddModelError(string.Empty, vehicleTypes.ErrorMessage ?? "Unable to load vehicle types.");
        Nations = nations.Value ?? [];
        VehicleTypes = vehicleTypes.Value ?? [];
        return Page();
    }
}
