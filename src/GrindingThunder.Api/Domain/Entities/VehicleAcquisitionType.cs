namespace GrindingThunder.Api.Domain.Entities;

/// <summary>
/// Defines how a vehicle is acquired in a specific research-tree version.
/// In War Thunder, vehicles can be standard researchable tech-tree vehicles,
/// premium (purchased with Golden Eagles or store packs), squadron (researched with squadron RP),
/// event rewards, store packs, or hidden/rare vehicles.
/// </summary>
public enum VehicleAcquisitionType
{
    Researchable = 0,
    Premium = 1,
    Squadron = 2,
    Event = 3,
    Pack = 4,
    Hidden = 5
}

