using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace GrindingThunder.Admin.Pages.Catalog.Vehicles;
[Authorize]
public class EditModel(IApiClient apiClient) : PageModel
{
    [BindProperty] public VehicleInputModel Input { get; set; } = new();
    public string? NotFoundMessage { get; private set; }
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var response = await apiClient.GetVehicleAsync(id, cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess || response.Value is null) { NotFoundMessage = response.ErrorMessage ?? "Vehicle was not found."; return Page(); }
        Input.Name = response.Value.Name; Input.ImageUrl = response.Value.ImageUrl; return Page();
    }
    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return Page();
        var response = await apiClient.UpdateVehicleAsync(id, new UpdateVehicleRequest(Input.Name, Input.ImageUrl), cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (response.IsNotFound) { NotFoundMessage = response.ErrorMessage ?? "Vehicle was not found."; return Page(); }
        if (!response.IsSuccess) { ModelState.AddModelError(string.Empty, response.ErrorMessage ?? "Unable to update vehicle."); return Page(); }
        TempData["SuccessMessage"] = $"Vehicle '{Input.Name}' was updated."; return RedirectToPage("Index");
    }
}
