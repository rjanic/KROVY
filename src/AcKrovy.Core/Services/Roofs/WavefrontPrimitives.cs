using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

// All coordinates are local XY; time is the perpendicular offset in millimetres.
internal readonly record struct WavefrontSource(int Id, RoofPoint2D Origin, double X, double Y)
{
    internal double Distance(RoofPoint2D p) => X * (p.X - Origin.X) + Y * (p.Y - Origin.Y);
    internal double Along(RoofPoint2D a, RoofPoint2D b) => Y * (b.X - a.X) - X * (b.Y - a.Y);
}

internal sealed record WavefrontVertex(int Id, int PreviousSource, int NextSource,
    int Anchor, RoofPoint2D Position, double Born, RoofPoint2D Velocity, bool Reflex, bool Coplanar)
{
    internal RoofPoint2D At(double time) => new(Position.X + (time - Born) * Velocity.X,
        Position.Y + (time - Born) * Velocity.Y);
}

// A loop has explicit cyclic predecessor/successor relationships. A split may
// create several loops; source IDs survive when an active source span splits.
internal sealed record WavefrontLoop(int Id, IReadOnlyList<WavefrontVertex> Vertices)
{
    internal WavefrontVertex Next(int i) => Vertices[(i + 1) % Vertices.Count];
    internal WavefrontVertex Previous(int i) => Vertices[(i + Vertices.Count - 1) % Vertices.Count];
}

internal enum WavefrontEventKind { Edge, Split }
internal sealed record WavefrontEvent(double Time, WavefrontEventKind Kind, int Vertex,
    int EdgeStart, int EdgeEnd, int Source);
internal readonly record struct WavefrontNode(RoofPoint2D Point, double Time);
internal readonly record struct WavefrontArc(int Start, int End, int LeftFace, int RightFace, RoofTopologyEdgeKind Kind);
internal readonly record struct WavefrontSpan(int Start, int End, int Source);

internal sealed class WavefrontNumerics
{
    internal WavefrontNumerics(double scale)
    {
        Tolerance = SimpleGableRoofGeometryTolerance.LengthTolerance(scale, scale);
        Arithmetic = Math.Max(1e-12, scale * 1e-14);
    }

    internal double Tolerance { get; }
    internal double Arithmetic { get; }
    internal static double Cross(RoofPoint2D a, RoofPoint2D b, RoofPoint2D c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    internal static bool Finite(RoofPoint2D p) => RoofTopologySolver.Finite(p.X) && RoofTopologySolver.Finite(p.Y);

    internal WavefrontVertex? Vertex(int id, int previous, int next, int anchor,
        WavefrontNode node, IReadOnlyList<WavefrontSource> sources)
    {
        var a = sources[previous];
        var b = sources[next];
        var turn = a.X * b.Y - a.Y * b.X;
        // Collinear same-direction descendants retain their separate source
        // ownership: the seam moves with the shared normal from its event anchor.
        // Opposite parallel fronts must disappear in the terminal-strip resolver.
        if (Math.Abs(turn) <= SimpleGableRoofGeometryTolerance.AngularTolerance &&
            a.X * b.X + a.Y * b.Y <= 0) return null;
        var sx = a.X + b.X;
        var sy = a.Y + b.Y;
        var denominator = (sx * sx + sy * sy) / 2;
        var velocity = new RoofPoint2D(sx / denominator, sy / denominator);
        if (!Finite(velocity) || Math.Abs(a.Distance(node.Point) - node.Time) > Tolerance ||
            Math.Abs(b.Distance(node.Point) - node.Time) > Tolerance) return null;
        return new(id, previous, next, anchor, node.Point, node.Time, velocity,
            turn < -SimpleGableRoofGeometryTolerance.AngularTolerance,
            Math.Abs(turn) <= SimpleGableRoofGeometryTolerance.AngularTolerance);
    }

    // Complete-link diameter check forbids swallowing chains of small features.
    internal int[]? Cluster(IReadOnlyList<RoofPoint2D> points)
    {
        if (points.Any(p => !Finite(p))) return null;
        var parents = Enumerable.Range(0, points.Count).ToArray();
        int Root(int i)
        {
            while (parents[i] != i) i = parents[i];
            return i;
        }
        for (var i = 0; i < points.Count; i++)
            for (var j = i + 1; j < points.Count; j++)
                if (points[i].DistanceTo(points[j]) <= Tolerance)
                {
                    var a = Root(i);
                    var b = Root(j);
                    parents[Math.Max(a, b)] = Math.Min(a, b);
                }
        var result = Enumerable.Range(0, points.Count).Select(Root).ToArray();
        for (var i = 0; i < points.Count; i++)
            for (var j = i + 1; j < points.Count; j++)
                if (result[i] == result[j] && points[i].DistanceTo(points[j]) > Tolerance) return null;
        return result;
    }

    internal bool OnSegment(RoofPoint2D p, RoofPoint2D a, RoofPoint2D b)
    {
        var length = a.DistanceTo(b);
        if (length <= Tolerance) return p.DistanceTo(a) <= Tolerance;
        var projection = ((p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y)) / length;
        return projection >= -Tolerance && projection <= length + Tolerance &&
            Math.Abs(Cross(a, b, p)) / length <= Tolerance;
    }
}
