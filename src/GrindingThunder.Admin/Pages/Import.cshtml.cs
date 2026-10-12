using GrindingThunder.Admin.Models;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;

namespace GrindingThunder.Admin.Pages;

[Authorize(Roles = "Editor,Admin")]
public class ImportModel(IApiClient apiClient, IMemoryCache cache) : PageModel
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;
    private sealed record PendingImport(string Username, Guid VersionId, string FileName, byte[] Bytes, bool AutoCreateMissingVehicles);

    public Guid VersionId { get; private set; }
    public IReadOnlyList<(Guid Id, string Label)> Drafts { get; private set; } = [];
    public ImportPreviewDto? Preview { get; private set; }
    public ImportCommitDto? Commit { get; private set; }
    public string? PreviewToken { get; private set; }
    public string? ErrorMessage { get; private set; }
    public bool AutoCreateMissingVehicles { get; private set; } = true;

    public async Task<IActionResult> OnGetAsync(Guid? versionId, CancellationToken cancellationToken)
    {
        await LoadDraftsAsync(cancellationToken);
        VersionId = versionId ?? Drafts.FirstOrDefault().Id;
        return Page();
    }

    public async Task<IActionResult> OnPostPreviewAsync(Guid versionId, IFormFile? file,
        bool autoCreateMissingVehicles, CancellationToken cancellationToken)
    {
        VersionId = versionId;
        AutoCreateMissingVehicles = autoCreateMissingVehicles;
        await LoadDraftsAsync(cancellationToken);
        if (!Drafts.Any(draft => draft.Id == versionId))
        {
            ErrorMessage = "Select a draft version from the list.";
            return Page();
        }

        if (file is null || file.Length == 0 || file.Length > MaxUploadBytes ||
            !new[] { ".csv", ".tsv", ".xlsx" }.Contains(Path.GetExtension(file.FileName), StringComparer.OrdinalIgnoreCase))
        {
            ErrorMessage = "Select a non-empty CSV, TSV, or XLSX file up to 10 MB.";
            return Page();
        }

        await using var input = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        using var upload = new MemoryStream(bytes);
        var result = await apiClient.PreviewImportAsync(versionId, upload, Path.GetFileName(file.FileName), autoCreateMissingVehicles, cancellationToken);
        if (result.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!result.IsSuccess || result.Value is null)
        {
            ErrorMessage = result.ErrorMessage ?? "Import preview failed.";
            return Page();
        }

        Preview = result.Value;
        if (Preview.IsValid)
        {
            PreviewToken = Guid.NewGuid().ToString("N");
            cache.Set(PreviewToken, new PendingImport(User.Identity!.Name!, versionId, Path.GetFileName(file.FileName),
                bytes, autoCreateMissingVehicles), TimeSpan.FromMinutes(15));
        }
        return Page();
    }

    public async Task<IActionResult> OnPostCommitAsync(Guid versionId, string previewToken, CancellationToken cancellationToken)
    {
        VersionId = versionId;
        await LoadDraftsAsync(cancellationToken);
        if (!Drafts.Any(draft => draft.Id == versionId) || string.IsNullOrWhiteSpace(previewToken) ||
            !cache.TryGetValue(previewToken, out PendingImport? pending) || pending is null ||
            pending.VersionId != versionId || pending.Username != User.Identity?.Name)
        {
            ErrorMessage = "The preview expired or the draft changed. Upload and preview the file again.";
            return Page();
        }

        using var upload = new MemoryStream(pending.Bytes);
        var result = await apiClient.CommitImportAsync(versionId, upload, pending.FileName, pending.AutoCreateMissingVehicles, cancellationToken);
        if (result.IsForbidden) return RedirectToPage("/AccessDenied");
        if (!result.IsSuccess || result.Value is null)
        {
            ErrorMessage = result.ErrorMessage ?? "Import commit failed.";
            return Page();
        }

        cache.Remove(previewToken);
        Commit = result.Value;
        return Page();
    }

    private async Task LoadDraftsAsync(CancellationToken cancellationToken)
    {
        var response = await apiClient.GetNationsWithVersionsAsync(cancellationToken);
        if (!response.IsSuccess || response.Value is null)
        {
            ErrorMessage = response.ErrorMessage ?? "Unable to list draft versions.";
            return;
        }

        Drafts = response.Value.SelectMany(nation => nation.ResearchTrees.SelectMany(tree =>
            tree.Versions.Where(version => version.Status == 0).Select(version =>
                (version.Id, $"{nation.Name} / {tree.VehicleTypeName} / {version.UpdateVersion}"))))
            .OrderBy(item => item.Item2).ToList();
    }
}

