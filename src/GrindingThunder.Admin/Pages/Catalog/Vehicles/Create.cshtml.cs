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
    public IReadOnlyList<ResearchTreeDto> Trees { get; private set; } = [];
    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadTreesAsync(cancellationToken);
    private async Task LoadTreesAsync(CancellationToken cancellationToken)
    {
        var response = await apiClient.GetResearchTreesAsync(cancellationToken);
        Trees = response.Value ?? [];
        if (!response.IsSuccess) ModelState.AddModelError(string.Empty, response.ErrorMessage ?? "Unable to load research trees.");
    }
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadTreesAsync(cancellationToken);
        if (Input.ResearchTreeId == Guid.Empty) ModelState.AddModelError("Input.ResearchTreeId", "Research tree is required.");
        if (!ModelState.IsValid) return Page();
        var response = await apiClient.CreateVehicleAsync(new CreateVehicleRequest(Input.ResearchTreeId, Input.Name, Input.ImageUrl), cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess) { ModelState.AddModelError(string.Empty, response.ErrorMessage ?? "Unable to create vehicle."); return Page(); }
        TempData["SuccessMessage"] = $"Vehicle '{response.Value?.Name ?? Input.Name}' was created."; return RedirectToPage("Index");
    }
}
