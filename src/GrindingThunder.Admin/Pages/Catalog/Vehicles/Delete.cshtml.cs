using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GrindingThunder.Admin.Pages.Trees;
namespace GrindingThunder.Admin.Pages.Catalog.Vehicles;
[Authorize(Roles = "Admin")]
public class DeleteModel(IApiClient apiClient) : TreeContextPageModel(apiClient)
{
    public VehicleDto? Item { get; private set; }
    public string? Message { get; private set; }
    public bool IsMissing { get; private set; }
    public async Task<IActionResult> OnGetAsync([FromRoute] Guid nationId, [FromRoute] Guid researchTreeId, [FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var contextError = await LoadTreeAsync(nationId, researchTreeId, cancellationToken);
        if (contextError is not null) return contextError;
        var response = await ApiClient.GetVehicleAsync(id, cancellationToken);
        var vehicleError = CheckVehicle(response);
        if (vehicleError is not null) return vehicleError;
        Item = response.Value; return Page();
    }
    public async Task<IActionResult> OnPostAsync([FromRoute] Guid nationId, [FromRoute] Guid researchTreeId, [FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var contextError = await LoadTreeAsync(nationId, researchTreeId, cancellationToken);
        if (contextError is not null) return contextError;
        var vehicleError = CheckVehicle(await ApiClient.GetVehicleAsync(id, cancellationToken));
        if (vehicleError is not null) return vehicleError;
        var response = await ApiClient.DeleteVehicleAsync(id, cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess) { Message = response.ErrorMessage ?? "Unable to delete vehicle."; IsMissing = response.IsNotFound; Item = (await ApiClient.GetVehicleAsync(id, cancellationToken)).Value; return Page(); }
        TempData["SuccessMessage"] = "Vehicle was deleted."; return BackToVehicles();
    }
}
