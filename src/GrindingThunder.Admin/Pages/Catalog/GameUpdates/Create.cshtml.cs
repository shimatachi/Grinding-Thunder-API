using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace GrindingThunder.Admin.Pages.Catalog.GameUpdates;
[Authorize]
public class CreateModel(IApiClient apiClient) : PageModel
{
    [BindProperty] public GameUpdateInputModel Input { get; set; } = new();
    public void OnGet() { }
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return Page();
        var response = await apiClient.CreateGameUpdateAsync(new CreateGameUpdateRequest(Input.Version, Input.Name, Input.ReleaseDate), cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess) { ModelState.AddModelError(string.Empty, response.ErrorMessage ?? "Unable to create game update."); return Page(); }
        TempData["SuccessMessage"] = $"Game update '{response.Value?.Version ?? Input.Version}' was created."; return RedirectToPage("Index");
    }
}
