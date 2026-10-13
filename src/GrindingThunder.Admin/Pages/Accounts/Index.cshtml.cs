using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrindingThunder.Admin.Pages.Accounts;

[Authorize(Roles = "Admin")]
public class IndexModel(IApiClient apiClient) : PageModel
{
    public IReadOnlyList<AdminAccountDto> Items { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var response = await apiClient.GetAdminAccountsAsync(cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (response.IsSuccess) Items = response.Value ?? [];
        else ErrorMessage = response.ErrorMessage;
        return Page();
    }
}
