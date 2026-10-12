using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrindingThunder.Admin.Pages.Catalog.Nations;

[Authorize]
public class EditModel(IApiClient apiClient) : PageModel
{
    [BindProperty] public NameInputModel Input { get; set; } = new();
    public string? NotFoundMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var response = await apiClient.GetNationAsync(id, cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (response.IsNotFound) { NotFoundMessage = response.ErrorMessage ?? "Nation was not found."; return Page(); }
        if (!response.IsSuccess || response.Value is null) { NotFoundMessage = response.ErrorMessage ?? "Unable to load nation."; return Page(); }
        Input.Name = response.Value.Name;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return Page();
        var response = await apiClient.UpdateNationAsync(id, new UpdateNationRequest(Input.Name), cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (response.IsNotFound) { NotFoundMessage = response.ErrorMessage ?? "Nation was not found."; return Page(); }
        if (!response.IsSuccess) { ModelState.AddModelError(string.Empty, response.ErrorMessage ?? "Unable to update nation."); return Page(); }
        TempData["SuccessMessage"] = $"Nation '{Input.Name}' was updated.";
        return RedirectToPage("Index");
    }
}
