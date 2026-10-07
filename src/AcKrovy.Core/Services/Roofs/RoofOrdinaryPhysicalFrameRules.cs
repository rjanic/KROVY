using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Reconstructs the directed Ordinary frame from the final Plan and roof plane.
/// UpperAxis is the roof-surface datum; section height points upwards and thickness
/// is constructed below it. No previous solid orientation participates.</summary>
public static class RoofOrdinaryPhysicalFrameRules
{
    public sealed record Frame(RoofPoint3D RoofPlaneOrigin, RoofSegment3D UpperAxis,
        RoofPoint3D LongitudinalAxis, RoofPoint3D RoofNormal,
        RoofPoint3D SectionWidthAxis, RoofPoint3D SectionHeightAxis);

    public static bool TryCreate(RoofSegment3D plan, RoofPoint3D origin,
        RoofFaceUnitNormal normal, out Frame? frame)
    {
        frame = null;
        if (!Finite(plan.Start) || !Finite(plan.End) || !Finite(origin) ||
            Math.Abs(plan.Start.Z) > 1e-7 || Math.Abs(plan.End.Z) > 1e-7 ||
            !RoofRafterPhysicalGeometry.IsValidUnitNormal(normal) || normal.Z <= 1e-9)
            return false;
        RoofPoint3D Lift(RoofPoint3D p) => new(p.X, p.Y, origin.Z -
            (normal.X * (p.X - origin.X) + normal.Y * (p.Y - origin.Y)) / normal.Z);
        var a = Lift(plan.Start);
        var b = Lift(plan.End);
        var delta = Subtract(b, a);
        var length = Length(delta);
        if (!Finite(a) || !Finite(b) || !Finite(length) || length <= 1e-7) return false;
        var l = Scale(delta, 1 / length);
        var n = new RoofPoint3D(normal.X, normal.Y, normal.Z);
        var w = Cross(n, l);
        var widthLength = Length(w);
        if (!Finite(widthLength) || widthLength <= 1e-9) return false;
        w = Scale(w, 1 / widthLength);
        var h = Cross(l, w);
        h = Scale(h, 1 / Length(h));
        frame = new(origin, new(a, b), l, n, w, h);
        return Dot(h, n) > 1 - 1e-9;
    }

    /// <summary>Read-only description using the exact persisted builder context.</summary>
    public static bool TryCreate(RoofOrdinaryPhysicalBuildState state,
        RoofAutomaticRafterPhysicalMember member, out Frame? frame)
    {
        if (!TryCreate(state, member.PlanAxis, member.SourceFaceIndex, out frame)) return false;
        if (member.SectionOrientation is { } transported)
            frame = frame! with { SectionWidthAxis = transported.NewFrame.WidthAxis,
                SectionHeightAxis = transported.NewFrame.HeightAxis };
        return true;
    }

    public static bool TryCreate(RoofOrdinaryPhysicalBuildState state, RoofSegment3D plan,
        int sourceFaceIndex, out Frame? frame)
    {
        frame = null;
        if (!RoofOrdinaryPhysicalBuildStateRules.IsValid(state)) return false;
        var topology = new RoofTopology(state.Nodes,
            state.Edges.Select(e => new RoofTopologyEdge(e.Start, e.End, e.Kind, e.Faces)),
            state.Faces.Select(f => new RoofTopologyFace(f.SourceEdge, f.Nodes)),
            state.BoundaryVertexCount, state.PitchDegrees);
        var face = topology.Faces.SingleOrDefault(f => f.SourceEdgeIndex == sourceFaceIndex);
        if (face is null || !RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(topology, face, out var n))
            return false;
        var origin = topology.Nodes[face.BoundaryNodeIndices[0]];
        return TryCreate(plan, new(origin.X, origin.Y, origin.Z + state.EaveElevationMm), n, out frame);
    }

    private static RoofPoint3D Subtract(RoofPoint3D a, RoofPoint3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static RoofPoint3D Scale(RoofPoint3D p, double s) => new(p.X * s, p.Y * s, p.Z * s);
    private static RoofPoint3D Cross(RoofPoint3D a, RoofPoint3D b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static double Dot(RoofPoint3D a, RoofPoint3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static double Length(RoofPoint3D p) => Math.Sqrt(Dot(p, p));
    private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    private static bool Finite(RoofPoint3D p) => Finite(p.X) && Finite(p.Y) && Finite(p.Z);
}
