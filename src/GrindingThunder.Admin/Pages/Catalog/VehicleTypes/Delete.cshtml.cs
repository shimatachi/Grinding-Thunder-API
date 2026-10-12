using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace GrindingThunder.Admin.Pages.Catalog.VehicleTypes;
[Authorize(Roles = "Admin")]
public class DeleteModel(IApiClient apiClient) : PageModel
{
    public VehicleTypeDto? Item { get; private set; }
    public string? Message { get; private set; }
    public bool IsMissing { get; private set; }
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var response = await apiClient.GetVehicleTypeAsync(id, cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess || response.Value is null) { Message = response.ErrorMessage ?? "Vehicle type was not found."; IsMissing = response.IsNotFound; return Page(); }
        Item = response.Value; return Page();
    }
    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        var response = await apiClient.DeleteVehicleTypeAsync(id, cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess) { Message = response.ErrorMessage ?? "Unable to delete vehicle type."; IsMissing = response.IsNotFound; Item = (await apiClient.GetVehicleTypeAsync(id, cancellationToken)).Value; return Page(); }
        TempData["SuccessMessage"] = "Vehicle type was deleted."; return RedirectToPage("Index");
    }
}
