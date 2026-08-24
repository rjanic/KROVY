namespace AcKrovy.Core.Models.Roofs;

/// <summary>One deterministic CAD-neutral rafter centerline clipped to a roof plane.</summary>
public sealed record RoofRafterGeometry(
    RafterRoofFace Face,
    int StationIndex,
    int StationCount,
    double StationFraction,
    double StationPositionMm,
    RoofPoint2D PlanStart,
    RoofPoint2D PlanEnd,
    RoofDirection2D RunDirection,
    double PlanLengthMm,
    double TrueLengthMm,
    double SlopeDegrees)
{
    public RoofGeneratedMemberKey LogicalKey =>
        new(RoofGeneratedTimberKind.Rafter, Face, StationIndex);
}
