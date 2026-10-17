using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GrindingThunder.Admin.Pages.Trees;
namespace GrindingThunder.Admin.Pages.Catalog.Vehicles;
[Authorize(Roles = "Admin,Editor")]
public class EditModel(IApiClient apiClient) : TreeContextPageModel(apiClient)
{
    [BindProperty] public VehicleInputModel Input { get; set; } = new();
    public string? NotFoundMessage { get; private set; }
    public async Task<IActionResult> OnGetAsync([FromRoute] Guid nationId, [FromRoute] Guid researchTreeId, [FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var contextError = await LoadTreeAsync(nationId, researchTreeId, cancellationToken);
        if (contextError is not null) return contextError;
        var response = await ApiClient.GetVehicleAsync(id, cancellationToken);
        var vehicleError = CheckVehicle(response);
        if (vehicleError is not null) return vehicleError;
        Input.Name = response.Value!.Name; Input.ImageUrl = response.Value.ImageUrl; Input.WikiUrl = response.Value.WikiUrl; return Page();
    }
    public async Task<IActionResult> OnPostAsync([FromRoute] Guid nationId, [FromRoute] Guid researchTreeId, [FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var contextError = await LoadTreeAsync(nationId, researchTreeId, cancellationToken);
        if (contextError is not null) return contextError;
        var vehicleError = CheckVehicle(await ApiClient.GetVehicleAsync(id, cancellationToken));
        if (vehicleError is not null) return vehicleError;
        if (!ModelState.IsValid) return Page();
        var response = await ApiClient.UpdateVehicleAsync(id, new UpdateVehicleRequest(Input.Name, Input.ImageUrl, Input.WikiUrl), cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (response.IsNotFound) { NotFoundMessage = response.ErrorMessage ?? "Vehicle was not found."; return Page(); }
        if (!response.IsSuccess) { ModelState.AddModelError(string.Empty, response.ErrorMessage ?? "Unable to update vehicle."); return Page(); }
        TempData["SuccessMessage"] = $"Vehicle '{Input.Name}' was updated."; return BackToVehicles();
    }
}
