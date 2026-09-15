using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

// All coordinates are local XY; time is the perpendicular offset in millimetres.
internal readonly record struct WavefrontSource(int Id, RoofPoint2D Origin, double X, double Y)
{
    internal double Distance(RoofPoint2D p) => X * (p.X - Origin.X) + Y * (p.Y - Origin.Y);
    internal double Along(RoofPoint2D a, RoofPoint2D b) => Y * (b.X - a.X) - X * (b.Y - a.Y);
}

internal enum WavefrontBoundaryCornerKind { None, Convex, Reflex }

// Boundary-corner provenance is independent of the current arc anchor. An
// event node becomes the next arc anchor, but it must not erase a uniquely
// surviving convex/reflex corner lineage.
internal readonly record struct WavefrontLineage(int BoundaryVertex, WavefrontBoundaryCornerKind Kind)
{
    internal static WavefrontLineage Internal => new(-1, WavefrontBoundaryCornerKind.None);
    internal bool HasBoundaryCorner => BoundaryVertex >= 0 && Kind != WavefrontBoundaryCornerKind.None;

    internal static WavefrontLineage ForSuccessor(
        IEnumerable<WavefrontVertex> localContactVertices,
        IEnumerable<WavefrontVertex> terminalContactVertices,
        int successorCount,
        int previousSource,
        int nextSource,
        bool successorReflex,
        bool successorCoplanar)
    {
        if (successorCount != 1)
        {
            return Internal;
        }

        var expectedKind = successorReflex
            ? WavefrontBoundaryCornerKind.Reflex
            : WavefrontBoundaryCornerKind.Convex;
        var local = localContactVertices
            .Where(vertex =>
                vertex.Lineage.Kind == expectedKind &&
                (vertex.PreviousSource == previousSource ||
                 vertex.NextSource == previousSource ||
                 vertex.PreviousSource == nextSource ||
                 vertex.NextSource == nextSource))
            .Select(vertex => vertex.Lineage)
            .Distinct()
            .ToArray();
        if (local.Length != 0)
        {
            return local.Length == 1 ? local[0] : Internal;
        }

        // A convex successor may be born at a reflex contact when the opposite
        // side of that contact collapses. Retain the reflex event provenance
        // only when the successor keeps the contact's directed outgoing source.
        // The structural resolver still requires an independently anchored Hip
        // path and incident-face continuity before this marker is materializable.
        if (!successorReflex && !successorCoplanar)
        {
            var outgoingReflex = localContactVertices
                .Where(vertex =>
                    vertex.Lineage.Kind == WavefrontBoundaryCornerKind.Reflex &&
                    vertex.NextSource == nextSource)
                .Select(vertex => vertex.Lineage)
                .Distinct()
                .ToArray();
            if (outgoingReflex.Length != 0)
            {
                return outgoingReflex.Length == 1 ? outgoingReflex[0] : Internal;
            }
        }

        // A split batch may terminate an intervening span at a second contact
        // root. In left-hand loop order only that root's lineage entering the
        // successor's outgoing source can continue across the batch.
        var terminal = terminalContactVertices
            .Where(vertex =>
                vertex.Lineage.Kind == expectedKind &&
                vertex.NextSource == nextSource)
            .Select(vertex => vertex.Lineage)
            .Distinct()
            .ToArray();
        return terminal.Length == 1 ? terminal[0] : Internal;
    }
}

internal sealed record WavefrontVertex(int Id, int PreviousSource, int NextSource,
    int Anchor, RoofPoint2D Position, double Born, RoofPoint2D Velocity, bool Reflex, bool Coplanar,
    WavefrontLineage Lineage)
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
internal readonly record struct WavefrontArc(
    int Start,
    int End,
    int LeftFace,
    int RightFace,
    RoofTopologyEdgeKind Kind,
    WavefrontLineage Lineage);
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
        WavefrontNode node, IReadOnlyList<WavefrontSource> sources, WavefrontLineage lineage)
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
        var reflex = turn < -SimpleGableRoofGeometryTolerance.AngularTolerance;
        var coplanar = Math.Abs(turn) <= SimpleGableRoofGeometryTolerance.AngularTolerance;
        var compatibleLineage = !coplanar &&
            (lineage.Kind == WavefrontBoundaryCornerKind.Convex && !reflex ||
             lineage.Kind == WavefrontBoundaryCornerKind.Reflex && reflex)
                ? lineage
                : WavefrontLineage.Internal;
        return new(id, previous, next, anchor, node.Point, node.Time, velocity,
            reflex, coplanar, compatibleLineage);
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
