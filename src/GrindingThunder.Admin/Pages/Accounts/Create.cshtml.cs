using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrindingThunder.Admin.Pages.Accounts;

[Authorize(Roles = "Admin")]
public class CreateModel(IApiClient apiClient) : PageModel
{
    [BindProperty] public AdminAccountCreateInputModel Input { get; set; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (Input.Roles.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Select at least one role.");
        }

        if (!ModelState.IsValid) return Page();

        var response = await apiClient.CreateAdminAccountAsync(
            new CreateAdminAccountRequest(Input.Username, Input.Password, Input.Roles), cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, response.ErrorMessage ?? "Unable to create account.");
            return Page();
        }

        TempData["SuccessMessage"] = $"Account '{response.Value?.Username ?? Input.Username}' was created.";
        return RedirectToPage("Index");
    }
}
