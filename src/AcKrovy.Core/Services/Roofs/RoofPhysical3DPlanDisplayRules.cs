using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Physical3D plan presentation contract: the source owner polyline is the sole
/// outer 2D perimeter. Physical WCS eaves are separate owned 3D Lines;
/// their coincident Face edges stay invisible to avoid duplicate 3D rendering.
/// </summary>
public static class RoofPhysical3DPlanDisplayRules
{
    /// <summary>
    /// True for disposable 2D display roles that duplicate the source footprint
    /// perimeter and must never be materialized for Physical3D-enabled hips.
    /// </summary>
    public static bool IsSourcePerimeterDisplayRole(RoofDisplayEdgeRole role) =>
        role is RoofDisplayEdgeRole.Eave0 or
            RoofDisplayEdgeRole.Eave1 or
            RoofDisplayEdgeRole.MonopitchLowEave or
            RoofDisplayEdgeRole.MonopitchHighEave;

    /// <summary>
    /// Filters owned Hip display edges for Physical3D: keep hip/ridge/valley only.
    /// </summary>
    public static IReadOnlyList<RoofDisplayEdge> FilterOwnedPhysical3DPlanEdges(
        IReadOnlyList<RoofDisplayEdge> edges)
    {
        if (edges is null)
        {
            throw new ArgumentNullException(nameof(edges));
        }

        return edges
            .Where(edge => !IsSourcePerimeterDisplayRole(edge.Role))
            .ToArray();
    }

    /// <summary>
    /// AutoCAD Face edge visibility for polygon vertices (pt0→pt1, pt1→pt2, pt2→pt3, pt3→pt0).
    /// Edges coincident with the face eave/perimeter are invisible.
    /// </summary>
    public static bool[] FaceEdgeVisibility(
        IReadOnlyList<RoofPoint3D> polygon,
        RoofSegment3D eaveSegment,
        double toleranceMm = SimpleGableRoofGeometryTolerance.CoordinateToleranceMm)
    {
        if (polygon is null)
        {
            throw new ArgumentNullException(nameof(polygon));
        }

        if (polygon.Count < 3)
        {
            throw new ArgumentException("Face polygon requires at least three points.", nameof(polygon));
        }

        // AutoCAD Face always takes four corners; triangular faces repeat the last point.
        var p0 = polygon[0];
        var p1 = polygon[1];
        var p2 = polygon[2];
        var p3 = polygon.Count >= 4 ? polygon[3] : polygon[2];
        return
        [
            !IsSameSegment(p0, p1, eaveSegment, toleranceMm),
            !IsSameSegment(p1, p2, eaveSegment, toleranceMm),
            !IsSameSegment(p2, p3, eaveSegment, toleranceMm),
            !IsSameSegment(p3, p0, eaveSegment, toleranceMm),
        ];
    }

    /// <summary>
    /// True when a generated segment lies on the source footprint perimeter
    /// (same XY endpoints as an eave), independent of Z.
    /// </summary>
    public static bool IsCoincidentWithFootprintPerimeterXy(
        RoofSegment3D candidate,
        IEnumerable<RoofSegment3D> footprintEaves,
        double toleranceMm = SimpleGableRoofGeometryTolerance.CoordinateToleranceMm)
    {
        if (footprintEaves is null)
        {
            throw new ArgumentNullException(nameof(footprintEaves));
        }

        foreach (var eave in footprintEaves)
        {
            if (IsSameSegmentXy(candidate.Start, candidate.End, eave, toleranceMm))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSameSegment(
        RoofPoint3D a,
        RoofPoint3D b,
        RoofSegment3D eave,
        double toleranceMm) =>
        (SamePoint(a, eave.Start, toleranceMm) && SamePoint(b, eave.End, toleranceMm)) ||
        (SamePoint(a, eave.End, toleranceMm) && SamePoint(b, eave.Start, toleranceMm));

    private static bool IsSameSegmentXy(
        RoofPoint3D a,
        RoofPoint3D b,
        RoofSegment3D eave,
        double toleranceMm) =>
        (SamePointXy(a, eave.Start, toleranceMm) && SamePointXy(b, eave.End, toleranceMm)) ||
        (SamePointXy(a, eave.End, toleranceMm) && SamePointXy(b, eave.Start, toleranceMm));

    private static bool SamePoint(RoofPoint3D left, RoofPoint3D right, double toleranceMm) =>
        Math.Abs(left.X - right.X) <= toleranceMm &&
        Math.Abs(left.Y - right.Y) <= toleranceMm &&
        Math.Abs(left.Z - right.Z) <= toleranceMm;

    private static bool SamePointXy(RoofPoint3D left, RoofPoint3D right, double toleranceMm) =>
        Math.Abs(left.X - right.X) <= toleranceMm &&
        Math.Abs(left.Y - right.Y) <= toleranceMm;
}
