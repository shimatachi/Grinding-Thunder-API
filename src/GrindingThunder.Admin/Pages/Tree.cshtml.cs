using System.Security.Claims;
using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrindingThunder.Admin.Pages;

/// <summary>
/// Anonymous, read-only browser for Published research-tree snapshots. Selection
/// is reconstructed exclusively from the publication-filtered public API.
/// Supports player authentication, progress overlay, and research route calculation.
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class TreeModel(IApiClient apiClient) : PageModel
{
    public IReadOnlyList<NationVersionsDto> Nations { get; private set; } = [];
    public NationVersionsDto? SelectedNation { get; private set; }
    public IReadOnlyList<TreeWithVersionsDto> ResearchTrees { get; private set; } = [];
    public TreeWithVersionsDto? SelectedResearchTree { get; private set; }
    public IReadOnlyList<TreeVersionListItemDto> Versions { get; private set; } = [];
    public TreeVersionListItemDto? SelectedVersion { get; private set; }
    public IReadOnlyList<PublicVehicleTreeEntryDto> Entries { get; private set; } = [];
    public VisualResearchTreeViewModel VisualTree { get; private set; } = new([], [], [], 0);
    public string? ErrorMessage { get; private set; }

    public bool IsPlayerAuthenticated { get; private set; }
    public string? PlayerUsername { get; private set; }
    public PlayerProgressResponse? InitialPlayerProgress { get; private set; }

    [BindProperty(SupportsGet = true)] public Guid? NationId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? ResearchTreeId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? VersionId { get; set; }

    public VisualTreeWorkspaceViewModel VisualWorkspace => new(
        VisualTree,
        false,
        SelectedVersion?.Id ?? Guid.Empty,
        SelectedNation is null || SelectedResearchTree is null
            ? "Published research tree"
            : $"{SelectedNation.Name} — {SelectedResearchTree.VehicleTypeName}",
        SelectedResearchTree?.Id,
        null,
        [],
        ErrorMessage,
        false,
        false,
        null,
        null);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var playerAuth = await HttpContext.AuthenticateAsync("PlayerAuth");
            if (playerAuth.Succeeded && playerAuth.Principal != null)
            {
                IsPlayerAuthenticated = true;
                PlayerUsername = playerAuth.Principal.Identity?.Name
                    ?? playerAuth.Principal.FindFirst(ClaimTypes.Name)?.Value;

                var progress = await apiClient.GetPlayerProgressAsync(cancellationToken: cancellationToken);
                if (progress.IsSuccess && progress.Value != null)
                {
                    InitialPlayerProgress = progress.Value;
                }
            }
        }
        catch
        {
            // Graceful fallback if authentication schemes or mock handlers differ
        }

        var catalog = await apiClient.GetPublicNationsWithVersionsAsync(cancellationToken);
        if (!catalog.IsSuccess || catalog.Value is null)
        {
            ErrorMessage = catalog.ErrorMessage ?? "Unable to load the published research-tree catalog.";
            return Page();
        }

        // Status is checked defensively even though the API contract already omits Drafts.
        Nations = catalog.Value
            .Where(nation => nation.ResearchTrees.Any(tree => tree.Versions.Any(version => version.Status == 1)))
            .OrderBy(nation => nation.Name)
            .ToList();
        if (Nations.Count == 0) return Page();

        SelectedNation = NationId.HasValue
            ? Nations.FirstOrDefault(nation => nation.Id == NationId.Value)
            : Nations[0];
        if (SelectedNation is null) return NotFound();
        NationId = SelectedNation.Id;

        ResearchTrees = SelectedNation.ResearchTrees
            .Where(tree => tree.Versions.Any(version => version.Status == 1))
            .OrderBy(tree => tree.VehicleTypeName)
            .ToList();
        SelectedResearchTree = ResearchTreeId.HasValue
            ? ResearchTrees.FirstOrDefault(tree => tree.Id == ResearchTreeId.Value)
            : ResearchTrees.FirstOrDefault();
        if (SelectedResearchTree is null) return ResearchTreeId.HasValue ? NotFound() : Page();
        ResearchTreeId = SelectedResearchTree.Id;

        // The API orders snapshots by release date/version descending, making the
        // first Published item the default current snapshot.
        Versions = SelectedResearchTree.Versions.Where(version => version.Status == 1).ToList();
        SelectedVersion = VersionId.HasValue
            ? Versions.FirstOrDefault(version => version.Id == VersionId.Value)
            : Versions.FirstOrDefault();
        if (SelectedVersion is null) return VersionId.HasValue ? NotFound() : Page();
        VersionId = SelectedVersion.Id;

        var entries = await apiClient.GetVehicleTreeAsync(
            SelectedNation.Id,
            SelectedVersion.Id,
            SelectedResearchTree.VehicleTypeName,
            cancellationToken);
        if (!entries.IsSuccess)
        {
            ErrorMessage = entries.ErrorMessage ?? "Unable to load the selected published snapshot.";
            return Page();
        }

        Entries = (entries.Value ?? [])
            .Where(entry => entry.ResearchTreeVersionId == SelectedVersion.Id)
            .OrderBy(entry => entry.TreeColumn)
            .ThenBy(entry => entry.TreeRow)
            .ToList();
        VisualTree = VisualTreeMapper.FromPublic(SelectedVersion.TreeRanks, Entries);
        return Page();
    }

    public async Task<IActionResult> OnPostPlayerLoginAsync([FromBody] PlayerLoginRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.UsernameOrEmail) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Username/email and password are required." });
        }

        var result = await apiClient.PlayerLoginAsync(request, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return StatusCode((int)result.StatusCode, new { message = result.ErrorMessage ?? "Invalid credentials." });
        }

        var payload = result.Value;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, payload.PlayerAccountId.ToString()),
            new(ClaimTypes.Name, payload.Username),
            new(ClaimTypes.Email, payload.Email),
            new("access_token", payload.Token)
        };

        var identity = new ClaimsIdentity(claims, "PlayerAuth");
        var principal = new ClaimsPrincipal(identity);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = payload.ExpiresAt
        };
        authProperties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = payload.Token }]);

        await HttpContext.SignInAsync("PlayerAuth", principal, authProperties);

        return new JsonResult(new
        {
            success = true,
            username = payload.Username,
            email = payload.Email,
            playerAccountId = payload.PlayerAccountId
        });
    }

    public async Task<IActionResult> OnPostPlayerRegisterAsync([FromBody] PlayerRegisterRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Username, email, and password are required." });
        }

        var result = await apiClient.PlayerRegisterAsync(request, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return StatusCode((int)result.StatusCode, new { message = result.ErrorMessage ?? "Registration failed." });
        }

        var payload = result.Value;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, payload.PlayerAccountId.ToString()),
            new(ClaimTypes.Name, payload.Username),
            new(ClaimTypes.Email, payload.Email),
            new("access_token", payload.Token)
        };

        var identity = new ClaimsIdentity(claims, "PlayerAuth");
        var principal = new ClaimsPrincipal(identity);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = payload.ExpiresAt
        };
        authProperties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = payload.Token }]);

        await HttpContext.SignInAsync("PlayerAuth", principal, authProperties);

        return new JsonResult(new
        {
            success = true,
            username = payload.Username,
            email = payload.Email,
            playerAccountId = payload.PlayerAccountId
        });
    }

    public async Task<IActionResult> OnPostPlayerLogoutAsync()
    {
        await HttpContext.SignOutAsync("PlayerAuth");
        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnGetPlayerProgressAsync(CancellationToken cancellationToken)
    {
        var result = await apiClient.GetPlayerProgressAsync(cancellationToken: cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return StatusCode((int)result.StatusCode, new { message = result.ErrorMessage ?? "Unable to load player progress." });
        }

        return new JsonResult(result.Value);
    }

    public async Task<IActionResult> OnPostUpdateProgressAsync([FromBody] UpdatePlayerProgressRequest request, CancellationToken cancellationToken)
    {
        if (request == null || request.UnlockedVehicleIds == null || request.Preferences == null)
        {
            return BadRequest(new { message = "Invalid progress payload." });
        }

        var result = await apiClient.UpdatePlayerProgressAsync(request, cancellationToken: cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return StatusCode((int)result.StatusCode, new { message = result.ErrorMessage ?? "Unable to update player progress." });
        }

        return new JsonResult(result.Value);
    }

    public async Task<IActionResult> OnPostCalculateAsync([FromBody] ResearchCalculationRequest request, CancellationToken cancellationToken)
    {
        if (request == null || request.ResearchTreeVersionId == Guid.Empty || request.TargetVehicleId == Guid.Empty)
        {
            return BadRequest(new { message = "ResearchTreeVersionId and TargetVehicleId are required." });
        }

        var result = await apiClient.CalculateResearchAsync(request, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return StatusCode((int)result.StatusCode, new { message = result.ErrorMessage ?? "Calculation failed." });
        }

        return new JsonResult(result.Value);
    }
}
