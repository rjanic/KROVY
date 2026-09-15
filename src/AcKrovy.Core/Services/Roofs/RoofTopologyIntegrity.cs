using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Validate concave output in its well-conditioned local frame before publication.</summary>
internal static class RoofTopologyIntegrity
{
    internal static bool Validate(RoofFootprint footprint, IReadOnlyList<WavefrontNode> local,
        IReadOnlyList<RoofPoint3D> nodes, IReadOnlyList<RoofTopologyEdge> edges,
        IReadOnlyList<RoofTopologyFace> faces, double tangent, WavefrontNumerics numeric)
    {
        var n = footprint.Vertices.Count;
        if (nodes.Count != local.Count || faces.Count != n || !RoofTopologySolver.Finite(tangent) ||
            local.Any(p => !WavefrontNumerics.Finite(p.Point) || !RoofTopologySolver.Finite(p.Time)) ||
            nodes.Any(p => !RoofTopologySolver.Finite(p.X) || !RoofTopologySolver.Finite(p.Y) || !RoofTopologySolver.Finite(p.Z)) ||
            local.Take(n).Any(p => p.Time != 0) || local.Skip(n).Any(p => p.Time <= 0)) return false;
        var keys = new HashSet<(int, int)>();
        var adjacency = Enumerable.Range(0, nodes.Count).Select(_ => new List<int>()).ToArray();
        var incidence = new Dictionary<(int, int), List<(int Face, bool Forward)>>();
        var area = 0d;
        foreach (var face in faces)
        {
            var source = face.SourceEdgeIndex;
            var cycle = face.BoundaryNodeIndices;
            if (source < 0 || source >= n || cycle.Count < 3 || cycle.Distinct().Count() != cycle.Count ||
                cycle.Any(i => i < 0 || i >= nodes.Count) || cycle[0] != source || cycle[1] != (source + 1) % n) return false;
            var a = local[cycle[0]].Point;
            var b = local[cycle[1]].Point;
            var length = a.DistanceTo(b);
            var faceArea = 0d;
            foreach (var index in cycle)
            {
                var run = WavefrontNumerics.Cross(a, b, local[index].Point) / length;
                if (Math.Abs(run - local[index].Time) > numeric.Tolerance ||
                    Math.Abs(run * tangent - nodes[index].Z) > numeric.Tolerance * Math.Max(1, tangent)) return false;
            }
            for (var i = 1; i + 1 < cycle.Count; i++)
                faceArea += WavefrontNumerics.Cross(a, local[cycle[i]].Point, local[cycle[i + 1]].Point) / 2;
            // Concave faces require a signed area sum, not a positive triangle fan.
            if (!RoofTopologySolver.Finite(faceArea) || faceArea <= numeric.Tolerance * numeric.Tolerance) return false;
            area += faceArea;
            for (var i = 0; i < cycle.Count; i++)
            {
                var start = cycle[i];
                var end = cycle[(i + 1) % cycle.Count];
                var key = (Math.Min(start, end), Math.Max(start, end));
                if (!incidence.TryGetValue(key, out var list)) incidence[key] = list = new();
                list.Add((source, start < end));
            }
        }
        if (!RoofTopologySolver.Finite(area) || Math.Abs(area - footprint.AreaMm2) >
            numeric.Tolerance * footprint.Edges.Sum(e => e.LengthMm) || incidence.Count != edges.Count) return false;
        foreach (var edge in edges)
        {
            var a = edge.StartNodeIndex;
            var b = edge.EndNodeIndex;
            if (a < 0 || b < 0 || a >= nodes.Count || b >= nodes.Count || a == b ||
                !keys.Add((Math.Min(a, b), Math.Max(a, b))) ||
                local[a].Point.DistanceTo(local[b].Point) <= numeric.Tolerance ||
                !RoofTopologySolver.Finite(nodes[a].DistanceTo(nodes[b]))) return false;
            if (edge.OriginatingBoundaryVertexIndex is { } origin)
            {
                if (origin < 0 || origin >= n ||
                    edge.Kind is not (RoofTopologyEdgeKind.Hip or RoofTopologyEdgeKind.Valley or RoofTopologyEdgeKind.Ridge)) return false;
                var turn = WavefrontNumerics.Cross(
                    footprint.Vertices[(origin + n - 1) % n],
                    footprint.Vertices[origin],
                    footprint.Vertices[(origin + 1) % n]);
                if (turn > 0d && edge.Kind != RoofTopologyEdgeKind.Hip ||
                    turn < 0d && edge.Kind is not (RoofTopologyEdgeKind.Valley or RoofTopologyEdgeKind.Ridge)) return false;
            }
            var eave = edge.Kind == RoofTopologyEdgeKind.Eave;
            if (edge.FaceIndices.Count != (eave ? 1 : 2) || edge.FaceIndices.Any(f => f < 0 || f >= n) ||
                edge.FaceIndices.Distinct().Count() != edge.FaceIndices.Count ||
                !incidence.TryGetValue((Math.Min(a, b), Math.Max(a, b)), out var uses) ||
                !uses.Select(u => u.Face).OrderBy(i => i).SequenceEqual(edge.FaceIndices) ||
                !eave && (uses.Count != 2 || uses[0].Forward == uses[1].Forward)) return false;
            if (!eave)
            {
                // Independent incident-plane check of the traced corner role.
                // A valley is locally the maximum of the two planes; a ridge/hip
                // is their minimum. Coincident planes have only an ownership seam.
                var left = footprint.Edges[uses.Single(u => u.Forward == (a < b)).Face];
                var right = footprint.Edges[uses.Single(u => u.Forward != (a < b)).Face];
                var dx = local[b].Point.X - local[a].Point.X;
                var dy = local[b].Point.Y - local[a].Point.Y;
                var length = local[a].Point.DistanceTo(local[b].Point);
                var nx = -(left.End.Y - left.Start.Y) / left.LengthMm + (right.End.Y - right.Start.Y) / right.LengthMm;
                var ny = (left.End.X - left.Start.X) / left.LengthMm - (right.End.X - right.Start.X) / right.LengthMm;
                var bend = (nx * -dy + ny * dx) / length;
                var coplanar = Math.Abs(bend) <= SimpleGableRoofGeometryTolerance.AngularTolerance;
                if (coplanar && edge.Kind != RoofTopologyEdgeKind.CoplanarSeam ||
                    !coplanar && bend > 0 && edge.Kind != RoofTopologyEdgeKind.Valley ||
                    !coplanar && bend < 0 && edge.Kind is not (RoofTopologyEdgeKind.Hip or RoofTopologyEdgeKind.Ridge))
                    return false;
                adjacency[a].Add(b);
                adjacency[b].Add(a);
            }
            var midpoint = new RoofPoint2D(local[a].Point.X / 2 + local[b].Point.X / 2,
                local[a].Point.Y / 2 + local[b].Point.Y / 2);
            if (!Inside(midpoint, footprint.Vertices, numeric)) return false;
        }
        if (edges.Count - n != nodes.Count - 1 || adjacency.Take(n).Any(l => l.Count != 1) ||
            adjacency.Skip(n).Any(l => l.Count < 3) || local.Any(p => !Inside(p.Point, footprint.Vertices, numeric))) return false;
        var seen = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(0);
        while (pending.Count > 0)
        {
            var index = pending.Pop();
            if (!seen.Add(index)) continue;
            foreach (var neighbor in adjacency[index]) pending.Push(neighbor);
        }
        if (seen.Count != nodes.Count) return false;
        for (var i = 0; i < edges.Count; i++)
            for (var j = i + 1; j < edges.Count; j++)
            {
                var first = edges[i];
                var second = edges[j];
                var common = new[] { first.StartNodeIndex, first.EndNodeIndex }.Intersect(
                    new[] { second.StartNodeIndex, second.EndNodeIndex }).ToArray();
                var a = local[first.StartNodeIndex].Point;
                var b = local[first.EndNodeIndex].Point;
                var c = local[second.StartNodeIndex].Point;
                var d = local[second.EndNodeIndex].Point;
                if (common.Length == 0)
                {
                    if (numeric.OnSegment(a, c, d) || numeric.OnSegment(b, c, d) ||
                        numeric.OnSegment(c, a, b) || numeric.OnSegment(d, a, b)) return false;
                    if (WavefrontNumerics.Cross(a, b, c) * WavefrontNumerics.Cross(a, b, d) < 0 &&
                        WavefrontNumerics.Cross(c, d, a) * WavefrontNumerics.Cross(c, d, b) < 0) return false;
                }
                else
                {
                    var otherA = first.StartNodeIndex == common[0] ? b : a;
                    var otherB = second.StartNodeIndex == common[0] ? d : c;
                    if (numeric.OnSegment(otherA, c, d) || numeric.OnSegment(otherB, a, b)) return false;
                }
            }
        return true;
    }

    private static bool Inside(RoofPoint2D point, IReadOnlyList<RoofPoint2D> polygon, WavefrontNumerics numeric)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            if (numeric.OnSegment(point, a, b)) return true;
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < a.X + (point.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y)) inside = !inside;
        }
        return inside;
    }
}
