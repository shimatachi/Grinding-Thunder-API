using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace GrindingThunder.Admin.Pages.Catalog.VehicleTypes;
[Authorize]
public class EditModel(IApiClient apiClient) : PageModel
{
    [BindProperty] public NameInputModel Input { get; set; } = new();
    public string? NotFoundMessage { get; private set; }
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var response = await apiClient.GetVehicleTypeAsync(id, cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess || response.Value is null) { NotFoundMessage = response.ErrorMessage ?? "Vehicle type was not found."; return Page(); }
        Input.Name = response.Value.Name; return Page();
    }
    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return Page();
        var response = await apiClient.UpdateVehicleTypeAsync(id, new UpdateVehicleTypeRequest(Input.Name), cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (response.IsNotFound) { NotFoundMessage = response.ErrorMessage ?? "Vehicle type was not found."; return Page(); }
        if (!response.IsSuccess) { ModelState.AddModelError(string.Empty, response.ErrorMessage ?? "Unable to update vehicle type."); return Page(); }
        TempData["SuccessMessage"] = $"Vehicle type '{Input.Name}' was updated."; return RedirectToPage("Index");
    }
}
