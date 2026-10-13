using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrindingThunder.Admin.Pages.Accounts;

[Authorize(Roles = "Admin")]
public class EditModel(IApiClient apiClient) : PageModel
{
    [BindProperty] public AdminAccountEditInputModel Input { get; set; } = new();
    [BindProperty] public AdminPasswordInputModel PasswordInput { get; set; } = new();

    public AdminAccountDto? Account { get; private set; }
    public string? Message { get; private set; }
    public bool IsMissing { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var failure = await LoadAsync(id, cancellationToken);
        if (failure is not null) return failure;

        Input = new AdminAccountEditInputModel
        {
            Username = Account!.Username,
            Roles = Account.Roles.ToList(),
            IsActive = Account.IsActive
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        // Manual validation keeps this form independent from the password form's model.
        if (string.IsNullOrWhiteSpace(Input.Username))
        {
            ModelState.AddModelError(string.Empty, "Username is required.");
        }
        if (Input.Roles.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Select at least one role.");
        }
        if (!ModelState.IsValid)
        {
            await LoadAsync(id, cancellationToken);
            return Page();
        }

        var response = await apiClient.UpdateAdminAccountAsync(
            id, new UpdateAdminAccountRequest(Input.Username, Input.Roles, Input.IsActive), cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, response.ErrorMessage ?? "Unable to update account.");
            await LoadAsync(id, cancellationToken);
            return Page();
        }

        TempData["SuccessMessage"] = "Account was updated.";
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostPasswordAsync(Guid id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(PasswordInput.Password) || PasswordInput.Password.Length < 8)
        {
            ModelState.AddModelError(string.Empty, "Password must be at least 8 characters long.");
            await LoadAsync(id, cancellationToken);
            if (Account is not null)
            {
                Input = new AdminAccountEditInputModel
                {
                    Username = Account.Username,
                    Roles = Account.Roles.ToList(),
                    IsActive = Account.IsActive
                };
            }
            return Page();
        }

        var response = await apiClient.ReplaceAdminPasswordAsync(
            id, new ReplaceAdminPasswordRequest(PasswordInput.Password), cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, response.ErrorMessage ?? "Unable to replace password.");
            await LoadAsync(id, cancellationToken);
            return Page();
        }

        TempData["SuccessMessage"] = "Password was replaced. The account must sign in again.";
        return RedirectToPage("Index");
    }

    private async Task<IActionResult?> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var response = await apiClient.GetAdminAccountAsync(id, cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!response.IsSuccess || response.Value is null)
        {
            Message = response.ErrorMessage ?? "Account was not found.";
            IsMissing = response.IsNotFound;
            return Page();
        }
        Account = response.Value;
        return null;
    }
}
