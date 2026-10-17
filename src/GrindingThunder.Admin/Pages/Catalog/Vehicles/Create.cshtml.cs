using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GrindingThunder.Admin.Pages.Trees;
namespace GrindingThunder.Admin.Pages.Catalog.Vehicles;
[Authorize(Roles = "Admin,Editor")]
public class CreateModel(IApiClient apiClient) : TreeContextPageModel(apiClient)
{
    [BindProperty] public VehicleInputModel Input { get; set; } = new();
    public async Task<IActionResult> OnGetAsync([FromRoute] Guid nationId, [FromRoute] Guid researchTreeId, CancellationToken cancellationToken) =>
        await LoadTreeAsync(nationId, researchTreeId, cancellationToken) ?? Page();

    public async Task<IActionResult> OnPostAsync([FromRoute] Guid nationId, [FromRoute] Guid researchTreeId, CancellationToken cancellationToken)
    {
        var contextError = await LoadTreeAsync(nationId, researchTreeId, cancellationToken);
        if (contextError is not null) return contextError;
        if (!ModelState.IsValid) return Page();
        var response = await ApiClient.CreateVehicleAsync(new CreateVehicleRequest(Tree.Id, Input.Name, Input.ImageUrl, Input.WikiUrl), cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess) { ModelState.AddModelError(string.Empty, response.ErrorMessage ?? "Unable to create vehicle."); return Page(); }
        TempData["SuccessMessage"] = $"Vehicle '{response.Value?.Name ?? Input.Name}' was created."; return BackToVehicles();
    }
}
