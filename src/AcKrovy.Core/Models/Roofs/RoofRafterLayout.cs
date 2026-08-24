namespace AcKrovy.Core.Models.Roofs;

/// <summary>Shared rafter layout over one or more bounded roof planes.</summary>
public sealed record RoofRafterLayout(
    double RequestedMaximumSpacingMm,
    double RafterPlanWidthMm,
    double StationSpanMm,
    double UsableCenterSpanMm,
    int IntervalCount,
    int StationCount,
    double ActualSpacingMm,
    RoofDirection2D StationDirection,
    IReadOnlyList<RoofRafterPlane> Planes,
    IReadOnlyList<RoofRafterGeometry> Rafters,
    string Signature);
