using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrindingThunder.Admin.Pages;

[Authorize]
public class CatalogModel : PageModel
{
    public void OnGet()
    {
    }
}

