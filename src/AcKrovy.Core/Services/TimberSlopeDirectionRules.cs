using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services;

/// <summary>
/// Resolves timber slope-arrow direction relative to a physical 3D member axis.
/// <see cref="TimberElementData.IsSlopeDirectionReversed"/> flips the arrow
/// relative to entity Start→End; when false the arrow follows Start→End.
/// </summary>
public static class TimberSlopeDirectionRules
{
    /// <summary>
    /// True when Start→End is uphill, so the DirectionalArrow must reverse to
    /// point physically downhill (higher elevation → lower elevation).
    /// Horizontal members (ΔZ within tolerance) keep the non-reversed default.
    /// </summary>
    public static bool ResolveIsReversedForDownhillDisplay(
        double startElevationMm,
        double endElevationMm,
        double elevationToleranceMm = 0d) =>
        endElevationMm - startElevationMm > elevationToleranceMm;

    public static bool ResolveIsReversedForDownhillDisplay(
        RoofSegment3D segment,
        double elevationToleranceMm = 0d) =>
        ResolveIsReversedForDownhillDisplay(
            segment.Start.Z,
            segment.End.Z,
            elevationToleranceMm);

    /// <summary>
    /// Plan-direction of the physical downhill sense of a 3D segment
    /// (high-Z endpoint toward low-Z endpoint). Equal elevations fall back to
    /// Start→End.
    /// </summary>
    public static (double X, double Y) ResolveDownhillPlanDirection(RoofSegment3D segment)
    {
        var highToLow = segment.Start.Z >= segment.End.Z
            ? (From: segment.Start, To: segment.End)
            : (From: segment.End, To: segment.Start);
        return (highToLow.To.X - highToLow.From.X, highToLow.To.Y - highToLow.From.Y);
    }
}
