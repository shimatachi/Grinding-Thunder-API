using System.ComponentModel.DataAnnotations;
using System.Net;

namespace GrindingThunder.Admin.Models;

public record NationDto(Guid Id, string Name);
public record CreateNationRequest(string Name);
public record UpdateNationRequest(string Name);

public record VehicleTypeDto(Guid Id, string Name);
public record CreateVehicleTypeRequest(string Name);
public record UpdateVehicleTypeRequest(string Name);

public record VehicleDto(Guid Id, string Name, string? ImageUrl,
    Guid ResearchTreeId, Guid NationId, string NationName, Guid VehicleTypeId, string VehicleTypeName, string? WikiUrl = null);
public record CreateVehicleRequest(Guid ResearchTreeId, string Name, string? ImageUrl = null, string? WikiUrl = null);
public record UpdateVehicleRequest(string Name, string? ImageUrl = null, string? WikiUrl = null);

public record GameUpdateDto(Guid Id, string Version, string? Name, DateOnly? ReleaseDate);
public record CreateGameUpdateRequest(string Version, string? Name = null, DateOnly? ReleaseDate = null);
public record UpdateGameUpdateRequest(string Version, string? Name = null, DateOnly? ReleaseDate = null);

public record ResearchTreeDto(
    Guid Id,
    Guid NationId,
    string NationName,
    Guid VehicleTypeId,
    string VehicleTypeName);
public record CreateResearchTreeRequest(Guid NationId, Guid VehicleTypeId);

public class NameInputModel
{
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;
}

public class VehicleInputModel : NameInputModel
{
    [Url(ErrorMessage = "Image URL must be a valid absolute URL.")]
    [Display(Name = "Image URL")]
    public string? ImageUrl { get; set; }

    [Url(ErrorMessage = "Wiki URL must be a valid absolute URL.")]
    [StringLength(2048, ErrorMessage = "Wiki URL cannot exceed 2048 characters.")]
    [Display(Name = "Wiki URL")]
    public string? WikiUrl { get; set; }
}

public class GameUpdateInputModel
{
    [Required(ErrorMessage = "Version is required.")]
    public string Version { get; set; } = string.Empty;

    public string? Name { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Release date")]
    public DateOnly? ReleaseDate { get; set; }
}

public class ResearchTreeInputModel
{
    [Display(Name = "Nation")]
    public Guid NationId { get; set; }

    [Display(Name = "Vehicle type")]
    public Guid VehicleTypeId { get; set; }
}

public record ApiResponse(HttpStatusCode StatusCode, string? ErrorMessage = null)
{
    public bool IsSuccess => (int)StatusCode is >= 200 and <= 299;
    public bool IsForbidden => StatusCode == HttpStatusCode.Forbidden;
    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;
}

public record ApiResponse<T>(HttpStatusCode StatusCode, T? Value = default, string? ErrorMessage = null)
    : ApiResponse(StatusCode, ErrorMessage);
