using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>One plane boundary plus a physical HIGH-to-LOW fall-direction arrow.</summary>
public static class MonopitchRoofWireframe
{
    public const int EdgeCount = 7;

    public static IReadOnlyList<RoofDisplayEdge> Create(
        MonopitchRoofGeometry geometry,
        double sourceElevation)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }
        if (!IsFinite(sourceElevation))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceElevation));
        }

        RoofPoint3D Wcs(RoofPoint3D point) =>
            new(point.X, point.Y, sourceElevation + point.Z);
        RoofSegment3D Segment(RoofPoint3D start, RoofPoint3D end) => new(Wcs(start), Wcs(end));
        var low = geometry.LowEave;
        var high = geometry.HighEave;
        var lowCenter = Midpoint(low.Start, low.End);
        var highCenter = Midpoint(high.Start, high.End);
        var arrowStart = Lerp(highCenter, lowCenter, 0.28d);
        var arrowTip = Lerp(highCenter, lowCenter, 0.72d);
        var backward = Normalize(Subtract(arrowStart, arrowTip));
        var transverse = Normalize(Subtract(low.End, low.Start));
        var wingBase = Add(arrowTip, Scale(backward, Math.Min(geometry.SpanMm * 0.12d, 450d)));
        var wing = Math.Min(geometry.SpanMm * 0.055d, 220d);

        return
        [
            new(RoofDisplayEdgeRole.MonopitchLowEave, Segment(low.Start, low.End)),
            new(RoofDisplayEdgeRole.MonopitchHighEave, Segment(high.Start, high.End)),
            new(RoofDisplayEdgeRole.MonopitchSlopeSide0, Segment(low.Start, high.Start)),
            new(RoofDisplayEdgeRole.MonopitchSlopeSide1, Segment(low.End, high.End)),
            new(RoofDisplayEdgeRole.MonopitchDirection, Segment(arrowStart, arrowTip)),
            new(RoofDisplayEdgeRole.MonopitchDirectionWing0,
                Segment(arrowTip, Add(wingBase, Scale(transverse, wing)))),
            new(RoofDisplayEdgeRole.MonopitchDirectionWing1,
                Segment(arrowTip, Add(wingBase, Scale(transverse, -wing)))),
        ];
    }

    private static RoofPoint3D Midpoint(RoofPoint3D first, RoofPoint3D second) =>
        new((first.X + second.X) / 2d, (first.Y + second.Y) / 2d, (first.Z + second.Z) / 2d);
    private static RoofPoint3D Lerp(RoofPoint3D first, RoofPoint3D second, double ratio) =>
        new(first.X + (second.X - first.X) * ratio,
            first.Y + (second.Y - first.Y) * ratio,
            first.Z + (second.Z - first.Z) * ratio);
    private static Vector3 Subtract(RoofPoint3D first, RoofPoint3D second) =>
        new(first.X - second.X, first.Y - second.Y, first.Z - second.Z);
    private static RoofPoint3D Add(RoofPoint3D point, Vector3 value) =>
        new(point.X + value.X, point.Y + value.Y, point.Z + value.Z);
    private static Vector3 Scale(Vector3 value, double scale) =>
        new(value.X * scale, value.Y * scale, value.Z * scale);
    private static Vector3 Normalize(Vector3 value)
    {
        var length = Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
        return new Vector3(value.X / length, value.Y / length, value.Z / length);
    }
    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
    private readonly record struct Vector3(double X, double Y, double Z);
}
