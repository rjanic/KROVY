using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// CAD-neutral Structural Member Spatial Axis Resolver.
///
/// Derives the 3D spatial axis (OS/centerline) from Plan2D XY + StructuralMemberElevationState.
/// The result is suitable for future JOIN, OFFSET, ARRAY, 2.5D inspection, and reports.
///
/// Future spatial JOIN rule:
///   JOIN requires 2D plan collinearity AND 3D spatial-axis compatibility.
///   Two members are join-compatible only if their spatial axes are collinear in 3D
///   (same direction AND zero discontinuity at the junction point).
///   Example VALID join: A axis Z 0→+2000, B axis Z +2000→+4000, same slope.
///   Example INVALID: same XY axis but 100 mm Z discontinuity, or different slopes.
/// </summary>
public static class StructuralMemberSpatialAxisRules
{
    /// <summary>
    /// Resolve the 3D spatial axis result from a plan axis (Z=0) and elevation state.
    /// Returns false if inputs are invalid or degenerate.
    /// </summary>
    public static bool TryResolve(
        RoofSegment3D planAxis,
        StructuralMemberElevationState elevationState,
        double widthMm,
        double heightMm,
        out StructuralMemberSpatialAxisResult? result)
    {
        result = null;
        if (!StructuralMemberElevationRules.IsValid(elevationState) ||
            !IsFinite(planAxis.Start) || !IsFinite(planAxis.End) ||
            Math.Abs(planAxis.Start.Z) > 1e-7 || Math.Abs(planAxis.End.Z) > 1e-7 ||
            !IsFinite(widthMm) || widthMm <= 0d ||
            !IsFinite(heightMm) || heightMm <= 0d) return false;

        var dx = planAxis.End.X - planAxis.Start.X;
        var dy = planAxis.End.Y - planAxis.Start.Y;
        var planLength = Math.Sqrt(dx * dx + dy * dy);
        if (planLength <= 1e-7) return false;

        var axisStart = elevationState.AxisStartElevationMm;
        var axisEnd = elevationState.AxisEndElevationMm;
        var slopeDeg = StructuralMemberElevationRules.DeriveSlopeDegrees(axisStart, axisEnd, planLength);
        var pitchDeg = StructuralMemberElevationRules.AbsolutePitchDegrees(slopeDeg);
        var heightAxisZ = StructuralMemberElevationRules.HeightAxisZ(pitchDeg);

        var spatialStart = new RoofPoint3D(planAxis.Start.X, planAxis.Start.Y, axisStart);
        var spatialEnd = new RoofPoint3D(planAxis.End.X, planAxis.End.Y, axisEnd);
        var spatialAxis = new RoofSegment3D(spatialStart, spatialEnd);

        // Longitudinal unit direction (Start → End in 3D).
        var sdx = spatialEnd.X - spatialStart.X;
        var sdy = spatialEnd.Y - spatialStart.Y;
        var sdz = spatialEnd.Z - spatialStart.Z;
        var len3d = Math.Sqrt(sdx * sdx + sdy * sdy + sdz * sdz);
        if (len3d < 1e-7) return false;
        var direction = new RoofPoint3D(sdx / len3d, sdy / len3d, sdz / len3d);

        result = new StructuralMemberSpatialAxisResult(
            planAxis,
            spatialAxis,
            direction,
            slopeDeg,
            heightAxisZ,
            widthMm,
            heightMm,
            SectionFrame: null); // section frame optionally enriched by AutoCAD layer from Build State
        return true;
    }

    /// <summary>
    /// Enrich a resolved axis result with the measured section frame from Build State.
    /// The frame provides precise HeightAxis direction from the physical solid.
    /// </summary>
    public static StructuralMemberSpatialAxisResult WithSectionFrame(
        StructuralMemberSpatialAxisResult result,
        RoofOrdinarySectionFrame frame) =>
        result with
        {
            SectionFrame = frame,
            // Override HeightAxisZ from the measured frame for maximum accuracy.
            SectionHeightAxisZ = frame.HeightAxis.Z,
        };

    /// <summary>
    /// Check future spatial JOIN compatibility between two members.
    /// Both must be collinear in 2D plan AND spatially continuous in 3D (no Z gap).
    /// A slope mismatch (different pitch) always makes them incompatible.
    /// Not HOST-tested; documented here for future implementation.
    /// </summary>
    public static bool AreSpatiallyJoinCompatible(
        StructuralMemberSpatialAxisResult a,
        StructuralMemberSpatialAxisResult b,
        double toleranceMm = 0.01d)
    {
        // Slopes must match (within tolerance).
        if (Math.Abs(a.SlopeDegrees - b.SlopeDegrees) > 0.01d) return false;
        // Spatial junction: end of A must equal start of B (or vice versa) within tolerance.
        var aEnd = a.SpatialAxis.End;
        var bStart = b.SpatialAxis.Start;
        var gap = Math.Sqrt(
            (aEnd.X - bStart.X) * (aEnd.X - bStart.X) +
            (aEnd.Y - bStart.Y) * (aEnd.Y - bStart.Y) +
            (aEnd.Z - bStart.Z) * (aEnd.Z - bStart.Z));
        if (gap <= toleranceMm) return true;
        // Try reversed direction.
        var aStart = a.SpatialAxis.Start;
        var bEnd = b.SpatialAxis.End;
        var gap2 = Math.Sqrt(
            (bEnd.X - aStart.X) * (bEnd.X - aStart.X) +
            (bEnd.Y - aStart.Y) * (bEnd.Y - aStart.Y) +
            (bEnd.Z - aStart.Z) * (bEnd.Z - aStart.Z));
        return gap2 <= toleranceMm;
    }

    private static bool IsFinite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);
    private static bool IsFinite(RoofPoint3D p) => IsFinite(p.X) && IsFinite(p.Y) && IsFinite(p.Z);
}
