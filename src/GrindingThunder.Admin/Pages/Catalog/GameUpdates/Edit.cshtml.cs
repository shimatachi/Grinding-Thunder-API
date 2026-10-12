using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace GrindingThunder.Admin.Pages.Catalog.GameUpdates;
[Authorize]
public class EditModel(IApiClient apiClient) : PageModel
{
    [BindProperty] public GameUpdateInputModel Input { get; set; } = new();
    public string? NotFoundMessage { get; private set; }
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var response = await apiClient.GetGameUpdateAsync(id, cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess || response.Value is null) { NotFoundMessage = response.ErrorMessage ?? "Game update was not found."; return Page(); }
        Input.Version = response.Value.Version; Input.Name = response.Value.Name; Input.ReleaseDate = response.Value.ReleaseDate; return Page();
    }
    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return Page();
        var response = await apiClient.UpdateGameUpdateAsync(id, new UpdateGameUpdateRequest(Input.Version, Input.Name, Input.ReleaseDate), cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (response.IsNotFound) { NotFoundMessage = response.ErrorMessage ?? "Game update was not found."; return Page(); }
        if (!response.IsSuccess) { ModelState.AddModelError(string.Empty, response.ErrorMessage ?? "Unable to update game update."); return Page(); }
        TempData["SuccessMessage"] = $"Game update '{Input.Version}' was updated."; return RedirectToPage("Index");
    }
}
