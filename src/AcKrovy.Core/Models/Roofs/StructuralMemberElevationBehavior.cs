namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Describes the spatial shape of the member's longitudinal axis in the elevation domain.
/// SlopedEndpoints: the two endpoint axis elevations are independently set; slope is derived.
/// UniformElevation: both endpoint axis elevations are equal; slope is always 0.
/// Designed to accommodate future horizontal structural members (wall plate, purlin, ridge beam, etc.).
/// </summary>
public enum StructuralMemberElevationBehavior
{
    /// <summary>Start and end axis elevations are independent. Slope = atan(ΔZ / planLength).</summary>
    SlopedEndpoints = 1,

    /// <summary>Both endpoint axis elevations are identical. Slope is always exactly 0.
    /// Reserved for future horizontal structural members.</summary>
    UniformElevation = 2,
}
