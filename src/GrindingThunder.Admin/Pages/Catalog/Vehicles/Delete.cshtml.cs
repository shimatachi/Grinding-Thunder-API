using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace GrindingThunder.Admin.Pages.Catalog.Vehicles;
[Authorize(Roles = "Admin")]
public class DeleteModel(IApiClient apiClient) : PageModel
{
    public VehicleDto? Item { get; private set; }
    public string? Message { get; private set; }
    public bool IsMissing { get; private set; }
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var response = await apiClient.GetVehicleAsync(id, cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess || response.Value is null) { Message = response.ErrorMessage ?? "Vehicle was not found."; IsMissing = response.IsNotFound; return Page(); }
        Item = response.Value; return Page();
    }
    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        var response = await apiClient.DeleteVehicleAsync(id, cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess) { Message = response.ErrorMessage ?? "Unable to delete vehicle."; IsMissing = response.IsNotFound; Item = (await apiClient.GetVehicleAsync(id, cancellationToken)).Value; return Page(); }
        TempData["SuccessMessage"] = "Vehicle was deleted."; return RedirectToPage("Index");
    }
}
