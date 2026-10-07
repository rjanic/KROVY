using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Directed right-handed timber section, independent of roof provenance.</summary>
public sealed record RoofOrdinarySectionFrame(RoofPoint3D LongitudinalAxis,
    RoofPoint3D WidthAxis, RoofPoint3D HeightAxis);

public sealed record RoofOrdinarySectionTransport(RoofOrdinarySectionFrame OldFrame,
    RoofOrdinarySectionFrame NewFrame, double MinimumAngleDegrees);

/// <summary>Accepted Independent frame and its minimum-rotation reference.
/// The reference is diagnostic; horizontal width is the approved yaw oracle.</summary>
public sealed record RoofOrdinarySectionOrientation(RoofOrdinarySectionFrame OldFrame,
    RoofOrdinarySectionFrame NewFrame, RoofOrdinarySectionFrame MinimumTransportFrame,
    double MinimumTransportAngleDegrees, bool DirectionChanged);

public static class RoofOrdinaryHorizontalSectionFrameRules
{
    public static bool TryCreate(RoofPoint3D direction, out RoofOrdinarySectionFrame? frame)
    {
        frame = null;
        var length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z);
        if (double.IsNaN(length) || double.IsInfinity(length) || length <= 1e-9) return false;
        var l = new RoofPoint3D(direction.X / length, direction.Y / length, direction.Z / length);
        var planLength = Math.Sqrt(l.X * l.X + l.Y * l.Y);
        if (planLength <= 1e-9) return false; // World-up frame is undefined for a vertical axis.
        var w = new RoofPoint3D(-l.Y / planLength, l.X / planLength, 0);
        var h = new RoofPoint3D(-l.Z * w.Y, l.Z * w.X, l.X * w.Y - l.Y * w.X);
        frame = new(l, w, h);
        return h.Z > 0 && RoofOrdinarySectionTransportRules.IsValid(frame);
    }
}

/// <summary>The shortest proper rotation maps L0 to L1 and transports W/H together.
/// An exact antiparallel axis has no unique shortest rotation; the old width axis
/// deterministically resolves this singular case without using a world-axis seed.</summary>
public static class RoofOrdinarySectionTransportRules
{
    public static bool IsValid(RoofOrdinarySectionFrame? f) => f is not null &&
        Unit(f.LongitudinalAxis) && Unit(f.WidthAxis) && Unit(f.HeightAxis) &&
        Math.Abs(Dot(f.LongitudinalAxis, f.WidthAxis)) <= 1e-7 &&
        Math.Abs(Dot(f.LongitudinalAxis, f.HeightAxis)) <= 1e-7 &&
        Math.Abs(Dot(f.WidthAxis, f.HeightAxis)) <= 1e-7 &&
        Dot(Cross(f.LongitudinalAxis, f.WidthAxis), f.HeightAxis) >= 1 - 1e-7;

    public static bool TryTransport(RoofOrdinarySectionFrame oldFrame, RoofPoint3D newDirection,
        out RoofOrdinarySectionTransport? transport)
    {
        transport = null;
        var newLength = Length(newDirection);
        if (!IsValid(oldFrame) || !Finite(newDirection) || newLength <= 1e-9 || !Finite(newLength))
            return false;
        var l0 = Normalize(oldFrame.LongitudinalAxis);
        var l1 = Normalize(newDirection);
        var cross = Cross(l0, l1);
        var sin = Length(cross);
        var cos = Math.Max(-1d, Math.Min(1d, Dot(l0, l1)));
        var angle = Math.Atan2(sin, cos);
        RoofPoint3D Rotate(RoofPoint3D p)
        {
            if (sin <= 1e-12 && cos >= 0) return p;
            var axis = sin <= 1e-12 ? Normalize(oldFrame.WidthAxis) : Scale(cross, 1 / sin);
            // Rodrigues: the same SO(3) rotation is applied to all three axes.
            return Add(Add(Scale(p, cos), Scale(Cross(axis, p), sin)),
                Scale(axis, Dot(axis, p) * (1 - cos)));
        }
        var rotatedW = Rotate(oldFrame.WidthAxis);
        var rotatedH = Rotate(oldFrame.HeightAxis);
        // Only remove floating-point longitudinal leakage, never prescribe roof roll.
        var w = Normalize(Subtract(rotatedW, Scale(l1, Dot(rotatedW, l1))));
        var h = Normalize(Cross(l1, w));
        var next = new RoofOrdinarySectionFrame(l1, w, h);
        if (!IsValid(next) || Dot(h, rotatedH) < 1 - 1e-7) return false;
        transport = new(oldFrame, next, angle * 180 / Math.PI);
        return true;
    }

    public static double ExtraTwistDegrees(RoofOrdinarySectionFrame transported,
        RoofOrdinarySectionFrame actual) => Math.Atan2(
            Dot(transported.LongitudinalAxis, Cross(transported.WidthAxis, actual.WidthAxis)),
            Dot(transported.WidthAxis, actual.WidthAxis)) * 180 / Math.PI;

    private static bool Unit(RoofPoint3D p) => Finite(p) && Math.Abs(Length(p) - 1) <= 1e-7;
    private static RoofPoint3D Normalize(RoofPoint3D p) => Scale(p, 1 / Length(p));
    private static RoofPoint3D Add(RoofPoint3D a, RoofPoint3D b) => new(a.X+b.X, a.Y+b.Y, a.Z+b.Z);
    private static RoofPoint3D Subtract(RoofPoint3D a, RoofPoint3D b) => new(a.X-b.X, a.Y-b.Y, a.Z-b.Z);
    private static RoofPoint3D Scale(RoofPoint3D p, double s) => new(p.X*s, p.Y*s, p.Z*s);
    private static RoofPoint3D Cross(RoofPoint3D a, RoofPoint3D b) =>
        new(a.Y*b.Z-a.Z*b.Y, a.Z*b.X-a.X*b.Z, a.X*b.Y-a.Y*b.X);
    private static double Dot(RoofPoint3D a, RoofPoint3D b) => a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    private static double Length(RoofPoint3D p) => Math.Sqrt(Dot(p,p));
    private static bool Finite(RoofPoint3D p) => Finite(p.X) && Finite(p.Y) && Finite(p.Z);
    private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
}
