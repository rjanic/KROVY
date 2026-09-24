namespace AcKrovy.Core.Models.Roofs;

/// <summary>A deterministic CAD-neutral upward unit normal of one roof face.</summary>
public readonly record struct RoofFaceUnitNormal(double X, double Y, double Z)
{
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
}

/// <summary>
/// Physical rafter section at one fixed horizontal station.
/// <see cref="UpperSurfacePoint"/> lies on the mathematical UPPER roof face;
/// <see cref="CenterlinePoint"/> is the timber centroid at the same XY;
/// section height is perpendicular to <see cref="FaceNormal"/> and appears as
/// vertical span height / n_z at that station.
/// </summary>
public sealed record RoofRafterPhysicalSection(
    RoofPoint3D CenterlinePoint,
    RoofFaceUnitNormal FaceNormal,
    double HeightMm,
    RoofPoint3D LowerSurfacePoint,
    RoofPoint3D UpperSurfacePoint);

/// <summary>
/// Inspector-ready physical placement of one horizontal purlin under a rafter.
/// Its section height is vertical in local/WCS Z and its top and bottom planes are
/// horizontal. Local values use the roof datum; relative values use the selected
/// elevation datum.
/// </summary>
public sealed record RoofPurlinPhysicalPlacement(
    RoofRafterPhysicalSection RafterSection,
    double RafterCenterLocalZMm,
    double RafterLowerSurfaceLocalZMm,
    double RafterUpperSurfaceLocalZMm,
    double SeatingDepthMm,
    double PurlinBottomLocalZMm,
    double PurlinCenterLocalZMm,
    double PurlinTopLocalZMm,
    double RafterCenterRelativeElevationMm,
    double RafterLowerSurfaceRelativeElevationMm,
    double RafterUpperSurfaceRelativeElevationMm,
    double PurlinBottomRelativeElevationMm,
    double PurlinCenterRelativeElevationMm,
    double PurlinTopRelativeElevationMm);

public enum RoofPurlinPhysicalPlacementError
{
    None = 0,
    InvalidFace,
    InvalidFaceNormal,
    InvalidCenterlinePoint,
    InvalidRafterHeight,
    InvalidPurlinHeight,
    InvalidSeatingDepth,
    InvalidRelativeElevationDatum,
    ImpossiblePlacement,
}

public sealed record RoofPurlinPhysicalPlacementResult(
    bool IsValid,
    RoofPurlinPhysicalPlacement? Placement,
    RoofPurlinPhysicalPlacementError Error);
