using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrindingThunder.Admin.Pages;

[Authorize(Roles = "Admin,Editor")]
public class AdministrationModel : PageModel
{
    public void OnGet() { }
}
