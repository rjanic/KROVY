namespace AcKrovy.Core.Models.Roofs;

public enum RoofRafterBoundaryRole
{
    Eave = 0,
    Ridge = 1,
    Hip = 2,
    Valley = 3,
}

/// <summary>One ordinary rafter centerline owned by a canonical topology face.</summary>
public sealed record RoofFaceRafterSegment(
    int SourceFaceIndex,
    int SourceEaveEdgeIndex,
    int StationIndex,
    int StationIntervalIndex,
    double StationDistanceMm,
    RoofPoint2D PlanStart,
    RoofPoint2D PlanEnd,
    RoofRafterBoundaryRole StartBoundaryRole,
    RoofRafterBoundaryRole EndBoundaryRole,
    double PlanLengthMm);

/// <summary>Canonical CAD-neutral ordinary-rafter layout over generic roof topology.</summary>
public sealed record RoofFaceRafterLayout(
    double RequestedSpacingMm,
    IReadOnlyList<RoofFaceRafterSegment> Segments,
    string Signature);

public enum RoofFaceRafterLayoutError
{
    None = 0,
    InvalidSpacing,
    InvalidTopology,
    TooManyStations,
}

public sealed record RoofFaceRafterLayoutResult(
    bool IsValid,
    RoofFaceRafterLayout? Layout,
    RoofFaceRafterLayoutError Error);
