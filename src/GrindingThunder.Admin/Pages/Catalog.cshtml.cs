using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace GrindingThunder.Admin.Pages;

[Authorize]
public class CatalogModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Administration");
}

