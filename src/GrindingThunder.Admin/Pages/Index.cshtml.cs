using System.Security.Claims;
using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrindingThunder.Admin.Pages;

[Authorize]
public class IndexModel(IApiClient apiClient) : PageModel
{
    private readonly IApiClient _apiClient = apiClient;

    public bool IsApiConnected { get; private set; }
    public string? ApiErrorMessage { get; private set; }
    public CurrentUserDto? ApiUser { get; private set; }
    public string ApiBaseUrl => _apiClient.BaseUrl;
    public string Username => User.Identity?.Name ?? "Unknown";
    public IReadOnlyList<string> LocalRoles { get; private set; } = [];

    public async Task OnGetAsync()
    {
        LocalRoles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct().ToList();

        var (isConnected, user, errorMessage) = await _apiClient.VerifySessionAsync();
        IsApiConnected = isConnected;
        ApiUser = user;
        ApiErrorMessage = errorMessage;
    }
}

