using System.ComponentModel.DataAnnotations;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Domain.Entities;
using GrindingThunder.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GrindingThunder.Api.Infrastructure.Services;

public class PlayerProgressService(ApplicationDbContext dbContext)
{
    public async Task<PlayerProgressResponse?> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.PlayerAccounts.AsNoTracking().Include(a => a.UnlockedVehicles)
            .SingleOrDefaultAsync(a => a.Id == playerId, cancellationToken);
        return account is null ? null : ToResponse(account);
    }

    public async Task<PlayerProgressUpdateResult> UpdateAsync(
        Guid playerId, UpdatePlayerProgressRequest request, CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), errors, validateAllProperties: true))
            return new(null, errors[0].ErrorMessage);
        if (!Validator.TryValidateObject(request.Preferences, new ValidationContext(request.Preferences), errors, validateAllProperties: true))
            return new(null, errors[0].ErrorMessage);

        var ids = request.UnlockedVehicleIds.ToHashSet();
        if (ids.Contains(Guid.Empty))
            return new(null, "Unlocked vehicle IDs must not be empty GUIDs.");
        var knownIds = await dbContext.Vehicles.Where(v => ids.Contains(v.Id))
            .Select(v => v.Id).ToListAsync(cancellationToken);
        if (knownIds.Count != ids.Count)
            return new(null, "One or more unlocked vehicle IDs do not exist in the stable vehicle catalog.");

        var account = await dbContext.PlayerAccounts.Include(a => a.UnlockedVehicles)
            .SingleOrDefaultAsync(a => a.Id == playerId, cancellationToken);
        if (account is null)
            return new(null, "Player account was not found.", NotFound: true);

        var previousIds = account.UnlockedVehicles.Select(v => v.VehicleId).ToHashSet();
        dbContext.PlayerUnlockedVehicles.RemoveRange(account.UnlockedVehicles.Where(v => !ids.Contains(v.VehicleId)));
        foreach (var id in ids.Except(previousIds))
            account.UnlockedVehicles.Add(new PlayerUnlockedVehicle { PlayerAccountId = playerId, VehicleId = id });
        account.OptimizationMode = request.Preferences.OptimizationMode;
        account.AverageRpPerMatch = request.Preferences.AverageRpPerMatch;
        account.AverageNetSlPerMatch = request.Preferences.AverageNetSlPerMatch;
        account.ProgressStamp = Guid.NewGuid();

        try
        {
            // EF saves the account and ownership diff in one transaction. The account's
            // concurrency stamp prevents two overlapping replacements from merging sets.
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(null, "Progress changed during this save. Reload progress and retry.", Conflict: true);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23503" or "23505" } ||
            (ex.InnerException?.Message.Contains("constraint failed", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return new(null, "The account, vehicle catalog, or progress changed during this save. Reload and retry.", Conflict: true);
        }

        return new(new PlayerProgressResponse(ids.Order().ToList(), request.Preferences));
    }

    private static PlayerProgressResponse ToResponse(PlayerAccount account) => new(
        account.UnlockedVehicles.Select(v => v.VehicleId).Order().ToList(),
        new(account.OptimizationMode, account.AverageRpPerMatch, account.AverageNetSlPerMatch));
}
