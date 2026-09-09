namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// CAD-neutral shared-Ridge station pairing audit for ordinary face rafters.
/// </summary>
public sealed record RoofRidgePairingReport(
    int RidgeComponentCount,
    int PairedComponentCount,
    int LeftEndpointCount,
    int RightEndpointCount,
    int MatchedStationCount,
    int UnmatchedLeft,
    int UnmatchedRight,
    double MaxPairGapMm,
    bool IsSatisfied,
    IReadOnlyList<RoofRidgePairingFailure> Failures)
{
    public static RoofRidgePairingReport Empty { get; } = new(
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0d,
        true,
        Array.Empty<RoofRidgePairingFailure>());
}

/// <summary>
/// One mismatched opposing Ridge endpoint pair (DEBUG / test diagnostics).
/// </summary>
public sealed record RoofRidgePairingFailure(
    int ComponentEdgeIndex,
    RoofPoint2D RidgeStart,
    RoofPoint2D RidgeEnd,
    int FaceA,
    int FaceB,
    double StationAMm,
    double StationBMm,
    RoofPoint2D PointA,
    RoofPoint2D PointB,
    double GapMm,
    RoofRafterBoundaryRole FaceAStartRole,
    RoofRafterBoundaryRole FaceAEndRole,
    RoofRafterBoundaryRole FaceBStartRole,
    RoofRafterBoundaryRole FaceBEndRole);
