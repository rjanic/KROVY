using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Model C placement for AttachedManual Structural timber.
/// The automatic fold is provenance only. The persisted frame is a rigid
/// snapshot of the source timber, translated or reflected by the native Plan XY clone.
/// Rebuild does not request an ordinary rafter physical model.
/// </summary>
public static class RoofStructuralManualPlacementRules
{
    private const double Tolerance = 1e-6;

    public static bool IsValid(RoofStructuralManualPlacement? placement)
    {
        if (placement is null) return false;
        var values = new[]
        {
            placement.AxisStartX, placement.AxisStartY, placement.AxisStartZ,
            placement.AxisEndX, placement.AxisEndY, placement.AxisEndZ,
            placement.SideX, placement.SideY, placement.SideZ,
            placement.UpX, placement.UpY, placement.UpZ,
            placement.SectionHeightMm,
        };
        if (values.Any(value => !IsFinite(value))) return false;
        if (placement.SectionHeightMm <= Tolerance) return false;
        var axis = Delta(
            placement.AxisStartX, placement.AxisStartY, placement.AxisStartZ,
            placement.AxisEndX, placement.AxisEndY, placement.AxisEndZ);
        if (Length(axis) <= Tolerance) return false;
        if (Math.Abs(Length((placement.SideX, placement.SideY, placement.SideZ)) - 1d) > 1e-4) return false;
        if (Math.Abs(Length((placement.UpX, placement.UpY, placement.UpZ)) - 1d) > 1e-4) return false;
        return true;
    }

    public static bool TryMatchRigidPlanCopy(
        RoofSegment3D sourcePlan, RoofSegment3D copiedPlan, out double dx, out double dy)
    {
        dx = copiedPlan.Start.X - sourcePlan.Start.X;
        dy = copiedPlan.Start.Y - sourcePlan.Start.Y;
        if (!IsFinite(dx) || !IsFinite(dy)) return false;
        if (Math.Abs(copiedPlan.Start.Z) > Tolerance || Math.Abs(copiedPlan.End.Z) > Tolerance ||
            Math.Abs(sourcePlan.Start.Z) > Tolerance || Math.Abs(sourcePlan.End.Z) > Tolerance)
            return false;
        return Math.Abs(copiedPlan.End.X - sourcePlan.End.X - dx) <= 1e-4 &&
               Math.Abs(copiedPlan.End.Y - sourcePlan.End.Y - dy) <= 1e-4 &&
               copiedPlan.LengthMm > Tolerance;
    }

    public static bool TryCaptureFrame(
        RoofStructuralRafterPolyhedron source, out RoofStructuralManualPlacement? placement)
    {
        placement = null;
        if (source.ConvexHalves.Count == 0 ||
            source.ConvexHalves[0].SourcePrismVertices.Count != 8)
            return false;
        var v = source.ConvexHalves[0].SourcePrismVertices;
        var side = (v[1].X - v[0].X, v[1].Y - v[0].Y, v[1].Z - v[0].Z);
        var up = (v[0].X - v[4].X, v[0].Y - v[4].Y, v[0].Z - v[4].Z);
        var sideLength = Length(side);
        var upLength = Length(up);
        if (sideLength <= Tolerance || upLength <= Tolerance) return false;
        var axisStart = ((v[0].X + v[1].X) * 0.5, (v[0].Y + v[1].Y) * 0.5, (v[0].Z + v[1].Z) * 0.5);
        var axisEnd = ((v[2].X + v[3].X) * 0.5, (v[2].Y + v[3].Y) * 0.5, (v[2].Z + v[3].Z) * 0.5);
        placement = new RoofStructuralManualPlacement(
            axisStart.Item1, axisStart.Item2, axisStart.Item3,
            axisEnd.Item1, axisEnd.Item2, axisEnd.Item3,
            side.Item1 / sideLength, side.Item2 / sideLength, side.Item3 / sideLength,
            up.Item1 / upLength, up.Item2 / upLength, up.Item3 / upLength,
            upLength);
        return IsValid(placement);
    }

    /// <summary>
    /// Recovers a vertical-plane reflection from corresponding native Line endpoints.
    /// Reflects the entire physical frame, including Side/Up; Z and section stay intact.
    /// Equal lengths alone are insufficient: reject a glide reflection or a changed segment.
    /// </summary>
    public static bool TryReflectFrame(
        RoofStructuralManualPlacement source,
        RoofSegment3D sourcePlan,
        RoofSegment3D mirroredPlan,
        out RoofStructuralManualPlacement? placement)
    {
        placement = null;
        if (!IsValid(source) || new[] { sourcePlan.Start, sourcePlan.End, mirroredPlan.Start, mirroredPlan.End }
                .Any(p => !IsFinite(p.X) || !IsFinite(p.Y) || !IsFinite(p.Z) || Math.Abs(p.Z) > Tolerance))
            return false;
        var sx = sourcePlan.End.X - sourcePlan.Start.X;
        var sy = sourcePlan.End.Y - sourcePlan.Start.Y;
        var mx = mirroredPlan.End.X - mirroredPlan.Start.X;
        var my = mirroredPlan.End.Y - mirroredPlan.Start.Y;
        var sourceLength = Math.Sqrt(sx * sx + sy * sy);
        var mirrorLength = Math.Sqrt(mx * mx + my * my);
        if (!IsFinite(sourceLength) || !IsFinite(mirrorLength) ||
            sourceLength <= Tolerance || mirrorLength <= Tolerance ||
            Math.Abs(sourceLength - mirrorLength) > 1e-4)
            return false;

        // The symmetric orthogonal XY matrix maps source direction to mirrored
        // direction with determinant -1. A rotation would have determinant +1.
        sx /= sourceLength;
        sy /= sourceLength;
        mx /= mirrorLength;
        my /= mirrorLength;
        var xx = sx * mx - sy * my;
        var xy = sx * my + sy * mx;
        var yy = -xx;
        // Midpoint displacement must be normal to the reflection plane (H*d = -d).
        // Work with differences to avoid multiplying large absolute WCS coordinates.
        var dx = (mirroredPlan.Start.X - sourcePlan.Start.X +
                  mirroredPlan.End.X - sourcePlan.End.X) * 0.5;
        var dy = (mirroredPlan.Start.Y - sourcePlan.Start.Y +
                  mirroredPlan.End.Y - sourcePlan.End.Y) * 0.5;
        if (Math.Abs(xx * dx + xy * dy + dx) > 1e-4 ||
            Math.Abs(xy * dx + yy * dy + dy) > 1e-4)
            return false;

        (double X, double Y) Point(double x, double y) => (
            mirroredPlan.Start.X + xx * (x - sourcePlan.Start.X) + xy * (y - sourcePlan.Start.Y),
            mirroredPlan.Start.Y + xy * (x - sourcePlan.Start.X) + yy * (y - sourcePlan.Start.Y));
        var start = Point(source.AxisStartX, source.AxisStartY);
        var end = Point(source.AxisEndX, source.AxisEndY);
        var reflected = source with
        {
            AxisStartX = start.X,
            AxisStartY = start.Y,
            AxisEndX = end.X,
            AxisEndY = end.Y,
            SideX = xx * source.SideX + xy * source.SideY,
            SideY = xy * source.SideX + yy * source.SideY,
            UpX = xx * source.UpX + xy * source.UpY,
            UpY = xy * source.UpX + yy * source.UpY,
        };
        if (!IsValid(reflected)) return false;
        placement = reflected;
        return true;
    }

    public static RoofStructuralManualPlacement Translate(
        RoofStructuralManualPlacement placement, double dx, double dy, double dz) =>
        placement with
        {
            AxisStartX = placement.AxisStartX + dx,
            AxisStartY = placement.AxisStartY + dy,
            AxisStartZ = placement.AxisStartZ + dz,
            AxisEndX = placement.AxisEndX + dx,
            AxisEndY = placement.AxisEndY + dy,
            AxisEndZ = placement.AxisEndZ + dz,
        };

    public static bool TryBuildPrism(
        RoofStructuralRole role,
        double widthMm,
        RoofStructuralManualPlacement placement,
        out RoofStructuralRafterPolyhedron? body,
        out string failureReason)
    {
        body = null;
        failureReason = string.Empty;
        if (role is not (RoofStructuralRole.Hip or RoofStructuralRole.Valley) ||
            !IsFinite(widthMm) || widthMm <= Tolerance || !IsValid(placement))
        {
            failureReason = "ManualPlacementInvalid";
            return false;
        }

        var axis = Delta(
            placement.AxisStartX, placement.AxisStartY, placement.AxisStartZ,
            placement.AxisEndX, placement.AxisEndY, placement.AxisEndZ);
        var half = widthMm * 0.5;
        var side = (placement.SideX * half, placement.SideY * half, placement.SideZ * half);
        var down = (
            -placement.UpX * placement.SectionHeightMm,
            -placement.UpY * placement.SectionHeightMm,
            -placement.UpZ * placement.SectionHeightMm);
        var start = (placement.AxisStartX, placement.AxisStartY, placement.AxisStartZ);
        var end = (placement.AxisEndX, placement.AxisEndY, placement.AxisEndZ);
        // 0/1 start top, 2/3 end top matching the source half's corresponding corners.
        // vertex[2] - vertex[0] is the axis, so corner 2 stays on the same side as corner 0.
        var top0 = Add(start, Neg(side));
        var top1 = Add(start, side);
        var top2 = Add(end, Neg(side));
        var top3 = Add(end, side);
        var vertices = new[]
        {
            ToPoint(top0), ToPoint(top1), ToPoint(top2), ToPoint(top3),
            ToPoint(Add(top0, down)), ToPoint(Add(top1, down)),
            ToPoint(Add(top2, down)), ToPoint(Add(top3, down)),
        };
        var upper = new RoofSegment3D(
            new RoofPoint3D(start.Item1, start.Item2, start.Item3),
            new RoofPoint3D(end.Item1, end.Item2, end.Item3));
        var halfBody = new RoofStructuralRafterConvexHalf(
            0, 1, vertices, vertices, vertices.Take(4).ToArray());
        var ridgeNormal = Normalize(Neg(axis));
        body = new RoofStructuralRafterPolyhedron(
            new RoofStructuralLogicalKey(role, 1, 2),
            RoofStructuralHeightMode.Explicit,
            new RoofStructuralRafterPhysicalModel(
                role, -1, upper, widthMm, placement.SectionHeightMm, placement.SectionHeightMm,
                true, null, Array.Empty<RoofStructuralPhysicalFace>(), vertices),
            Array.Empty<int>(),
            0d,
            new[] { halfBody },
            Array.Empty<RoofStructuralRafterClipPlane>(),
            new RoofStructuralRafterClipPlane(
                new RoofPoint3D(end.Item1, end.Item2, end.Item3),
                new RoofPoint3D(ridgeNormal.Item1, ridgeNormal.Item2, ridgeNormal.Item3)),
            null,
            LowerEndCutMode.Vertical,
            Array.Empty<RoofStructuralRafterClipPlane>(),
            Array.Empty<RoofStructuralRafterClipPlane>());
        _ = axis;
        return true;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static (double X, double Y, double Z) Delta(
        double ax, double ay, double az, double bx, double by, double bz) => (bx - ax, by - ay, bz - az);
    private static double Length((double X, double Y, double Z) v) =>
        Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
    private static (double, double, double) Neg((double X, double Y, double Z) v) => (-v.X, -v.Y, -v.Z);
    private static (double, double, double) Add(
        (double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        (a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    private static (double, double, double) Normalize((double X, double Y, double Z) v)
    {
        var length = Length(v);
        return length <= Tolerance ? (0, 0, 0) : (v.X / length, v.Y / length, v.Z / length);
    }
    private static RoofPoint3D ToPoint((double X, double Y, double Z) v) => new(v.X, v.Y, v.Z);
}
