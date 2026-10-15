using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GrindingThunder.Admin.Pages.Trees;
namespace GrindingThunder.Admin.Pages.Catalog.Vehicles;
[Authorize(Roles = "Admin,Editor,Publisher")]
public class IndexModel(IApiClient apiClient) : TreeContextPageModel(apiClient)
{
    public IReadOnlyList<VehicleDto> Items { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public async Task<IActionResult> OnGetAsync([FromRoute] Guid nationId, [FromRoute] Guid researchTreeId, CancellationToken cancellationToken)
    {
        var contextError = await LoadTreeAsync(nationId, researchTreeId, cancellationToken);
        if (contextError is not null) return contextError;
        var response = await ApiClient.GetTreeVehiclesAsync(Tree.Id, cancellationToken);
        if (response.IsForbidden) return RedirectToPage("/AccessDenied");
        if (response.IsSuccess) Items = (response.Value ?? []).Where(v => v.ResearchTreeId == Tree.Id).ToList(); else ErrorMessage = response.ErrorMessage;
        return Page();
    }
}
