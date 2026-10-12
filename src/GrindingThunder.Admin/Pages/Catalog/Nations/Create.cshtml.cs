using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrindingThunder.Admin.Pages.Catalog.Nations;

[Authorize]
public class CreateModel(IApiClient apiClient) : PageModel
{
    [BindProperty] public NameInputModel Input { get; set; } = new();
    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return Page();
        var response = await apiClient.CreateNationAsync(new CreateNationRequest(Input.Name), cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess) { ModelState.AddModelError(string.Empty, response.ErrorMessage ?? "Unable to create nation."); return Page(); }
        TempData["SuccessMessage"] = $"Nation '{response.Value?.Name ?? Input.Name}' was created.";
        return RedirectToPage("Index");
    }
}
