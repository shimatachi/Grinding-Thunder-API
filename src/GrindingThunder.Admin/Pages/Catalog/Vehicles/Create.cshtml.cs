using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace GrindingThunder.Admin.Pages.Catalog.Vehicles;
[Authorize]
public class CreateModel(IApiClient apiClient) : PageModel
{
    [BindProperty] public VehicleInputModel Input { get; set; } = new();
    public void OnGet() { }
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return Page();
        var response = await apiClient.CreateVehicleAsync(new CreateVehicleRequest(Input.Name, Input.ImageUrl), cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess) { ModelState.AddModelError(string.Empty, response.ErrorMessage ?? "Unable to create vehicle."); return Page(); }
        TempData["SuccessMessage"] = $"Vehicle '{response.Value?.Name ?? Input.Name}' was created."; return RedirectToPage("Index");
    }
}
