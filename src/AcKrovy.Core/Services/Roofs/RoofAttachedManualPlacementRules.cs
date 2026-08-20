using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Roof-footprint local U/V frame for AttachedManual placement. Uses the first footprint
/// edge as axis U; does not assume centered ridge or equal pitches.
/// </summary>
public readonly record struct RoofFootprintAttachmentFrame(
    RoofPoint3D Origin,
    RoofPoint3D AxisU,
    RoofPoint3D AxisV,
    RoofPoint3D AxisW,
    double ScaleU,
    double ScaleV)
{
    public static bool TryCreate(
        IReadOnlyList<RoofPoint2D> footprintVertices,
        double elevationMm,
        RoofPoint3D planeNormal,
        out RoofFootprintAttachmentFrame frame)
    {
        frame = default;
        if (footprintVertices is null || footprintVertices.Count < 2)
        {
            return false;
        }

        var origin = new RoofPoint3D(footprintVertices[0].X, footprintVertices[0].Y, elevationMm);
        var second = new RoofPoint3D(footprintVertices[1].X, footprintVertices[1].Y, elevationMm);
        if (!TryNormalize(AxisFrom(origin, second), out var axisU))
        {
            return false;
        }

        if (!TryNormalize(planeNormal, out var axisW))
        {
            return false;
        }

        var axisV = Cross(axisW, axisU);
        if (!TryNormalize(axisV, out axisV))
        {
            return false;
        }

        axisU = Cross(axisV, axisW);
        if (!TryNormalize(axisU, out axisU))
        {
            return false;
        }

        var scaleU = Distance(origin, second);
        var oppositeCorner = footprintVertices[footprintVertices.Count - 1];
        var corner = new RoofPoint3D(oppositeCorner.X, oppositeCorner.Y, elevationMm);
        var scaleV = Math.Abs(Dot(Subtract(corner, origin), axisV));
        if (scaleU <= 0.000001d || scaleV <= 0.000001d)
        {
            return false;
        }

        frame = new RoofFootprintAttachmentFrame(origin, axisU, axisV, axisW, scaleU, scaleV);
        return true;
    }

    public RoofPoint3D WorldToLocal(RoofPoint3D world)
    {
        var delta = Subtract(world, Origin);
        return new RoofPoint3D(
            Dot(delta, AxisU) / ScaleU,
            Dot(delta, AxisV) / ScaleV,
            Dot(delta, AxisW));
    }

    public RoofPoint3D LocalToWorld(RoofPoint3D local) =>
        Add(
            Origin,
            Add(
                Scale(AxisU, local.X * ScaleU),
                Add(Scale(AxisV, local.Y * ScaleV), Scale(AxisW, local.Z))));

    public RoofPoint3D WorldAnchorToNormalized(RoofPoint3D world) => WorldToLocal(world);

    public RoofPoint3D NormalizedAnchorToWorld(RoofPoint3D normalizedAnchor) => LocalToWorld(normalizedAnchor);

    public void DecomposeVectorMm(RoofPoint3D vector, out double alongU, out double alongV, out double alongW)
    {
        alongU = Dot(vector, AxisU);
        alongV = Dot(vector, AxisV);
        alongW = Dot(vector, AxisW);
    }

    public RoofPoint3D ComposeVectorMm(double alongU, double alongV, double alongW) =>
        Add(Scale(AxisU, alongU), Add(Scale(AxisV, alongV), Scale(AxisW, alongW)));

    private static double Distance(RoofPoint3D left, RoofPoint3D right) =>
        Math.Sqrt(Dot(Subtract(left, right), Subtract(left, right)));

    private static RoofPoint3D AxisFrom(RoofPoint3D start, RoofPoint3D end) =>
        new(end.X - start.X, end.Y - start.Y, end.Z - start.Z);

    private static double Dot(RoofPoint3D left, RoofPoint3D right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    private static RoofPoint3D Subtract(RoofPoint3D left, RoofPoint3D right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static RoofPoint3D Add(RoofPoint3D left, RoofPoint3D right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static RoofPoint3D Scale(RoofPoint3D vector, double scalar) =>
        new(vector.X * scalar, vector.Y * scalar, vector.Z * scalar);

    private static RoofPoint3D Cross(RoofPoint3D left, RoofPoint3D right) =>
        new(
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);

    private static bool TryNormalize(RoofPoint3D vector, out RoofPoint3D normalized)
    {
        var length = Math.Sqrt(Dot(vector, vector));
        if (length <= 0.000001d)
        {
            normalized = default;
            return false;
        }

        normalized = Scale(vector, 1d / length);
        return true;
    }
}

/// <summary>
/// Remaps AttachedManual geometry across a supported source resize using footprint-local coordinates.
/// </summary>
public static class RoofAttachedManualPlacementRules
{
    public const double GeometryLengthToleranceMm = RoofGeneratedMemberOverrideMath.LengthToleranceMm;
    public const double GeometryAngleToleranceRadians = RoofGeneratedMemberOverrideMath.AngleToleranceRadians;

    public readonly record struct RemapMetrics(
        double OldLengthMm,
        double NewLengthMm,
        double OldLocalAngleRadians,
        double NewLocalAngleRadians,
        double AnchorU,
        double AnchorV,
        bool GeometryPreserved);

    public static bool TryMapPoint(
        RoofPoint3D worldPoint,
        IReadOnlyList<RoofPoint2D> sourceFootprintVertices,
        double sourceElevationMm,
        RoofPoint3D sourcePlaneNormal,
        IReadOnlyList<RoofPoint2D> targetFootprintVertices,
        double targetElevationMm,
        RoofPoint3D targetPlaneNormal,
        out RoofPoint3D mappedWorld)
    {
        mappedWorld = default;
        if (!RoofFootprintAttachmentFrame.TryCreate(
                sourceFootprintVertices,
                sourceElevationMm,
                sourcePlaneNormal,
                out var sourceFrame) ||
            !RoofFootprintAttachmentFrame.TryCreate(
                targetFootprintVertices,
                targetElevationMm,
                targetPlaneNormal,
                out var targetFrame))
        {
            return false;
        }

        var local = sourceFrame.WorldToLocal(worldPoint);
        mappedWorld = targetFrame.LocalToWorld(local);
        return true;
    }

    public static bool TryMapSegment(
        RoofPoint3D start,
        RoofPoint3D end,
        IReadOnlyList<RoofPoint2D> sourceFootprintVertices,
        double sourceElevationMm,
        RoofPoint3D sourcePlaneNormal,
        IReadOnlyList<RoofPoint2D> targetFootprintVertices,
        double targetElevationMm,
        RoofPoint3D targetPlaneNormal,
        out RoofPoint3D mappedStart,
        out RoofPoint3D mappedEnd) =>
        TryMapSegment(
            start,
            end,
            sourceFootprintVertices,
            sourceElevationMm,
            sourcePlaneNormal,
            targetFootprintVertices,
            targetElevationMm,
            targetPlaneNormal,
            out mappedStart,
            out mappedEnd,
            out _);

    /// <summary>
    /// Rigid remap: normalized anchor moves with footprint; segment vector stays fixed in local mm.
    /// </summary>
    public static bool TryMapSegment(
        RoofPoint3D start,
        RoofPoint3D end,
        IReadOnlyList<RoofPoint2D> sourceFootprintVertices,
        double sourceElevationMm,
        RoofPoint3D sourcePlaneNormal,
        IReadOnlyList<RoofPoint2D> targetFootprintVertices,
        double targetElevationMm,
        RoofPoint3D targetPlaneNormal,
        out RoofPoint3D mappedStart,
        out RoofPoint3D mappedEnd,
        out RemapMetrics metrics)
    {
        mappedStart = default;
        mappedEnd = default;
        metrics = default;
        if (!RoofFootprintAttachmentFrame.TryCreate(
                sourceFootprintVertices,
                sourceElevationMm,
                sourcePlaneNormal,
                out var sourceFrame) ||
            !RoofFootprintAttachmentFrame.TryCreate(
                targetFootprintVertices,
                targetElevationMm,
                targetPlaneNormal,
                out var targetFrame))
        {
            return false;
        }

        var delta = Subtract(end, start);
        var oldLength = Distance(start, end);
        var midpoint = Scale(Add(start, end), 0.5d);
        var anchor = sourceFrame.WorldAnchorToNormalized(midpoint);
        sourceFrame.DecomposeVectorMm(delta, out var duMm, out var dvMm, out var dwMm);
        var oldLocalAngle = Math.Atan2(dvMm, duMm);

        var midpointPrime = targetFrame.NormalizedAnchorToWorld(anchor);
        var deltaPrime = targetFrame.ComposeVectorMm(duMm, dvMm, dwMm);
        var halfDeltaPrime = Scale(deltaPrime, 0.5d);
        mappedStart = Subtract(midpointPrime, halfDeltaPrime);
        mappedEnd = Add(midpointPrime, halfDeltaPrime);

        var newLength = Distance(mappedStart, mappedEnd);
        targetFrame.DecomposeVectorMm(deltaPrime, out var duPrime, out var dvPrime, out _);
        var newLocalAngle = Math.Atan2(dvPrime, duPrime);
        var lengthPreserved = Math.Abs(newLength - oldLength) <= GeometryLengthToleranceMm;
        var anglePreserved = AngleDelta(oldLocalAngle, newLocalAngle) <= GeometryAngleToleranceRadians;
        metrics = new RemapMetrics(
            oldLength,
            newLength,
            oldLocalAngle,
            newLocalAngle,
            anchor.X,
            anchor.Y,
            lengthPreserved && anglePreserved);
        return metrics.GeometryPreserved;
    }

    private static double Distance(RoofPoint3D left, RoofPoint3D right) =>
        Math.Sqrt(Dot(Subtract(left, right), Subtract(left, right)));

    private static RoofPoint3D Subtract(RoofPoint3D left, RoofPoint3D right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static RoofPoint3D Add(RoofPoint3D left, RoofPoint3D right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static RoofPoint3D Scale(RoofPoint3D vector, double scalar) =>
        new(vector.X * scalar, vector.Y * scalar, vector.Z * scalar);

    private static double Dot(RoofPoint3D left, RoofPoint3D right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    private static double AngleDelta(double leftRadians, double rightRadians)
    {
        var delta = rightRadians - leftRadians;
        while (delta > Math.PI)
        {
            delta -= 2d * Math.PI;
        }

        while (delta < -Math.PI)
        {
            delta += 2d * Math.PI;
        }

        return Math.Abs(delta);
    }
}
