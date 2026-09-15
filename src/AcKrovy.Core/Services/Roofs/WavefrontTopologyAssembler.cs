using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

internal static class WavefrontTopologyAssembler
{
    internal static RoofTopologyResult Build(RoofFootprint footprint, RoofPoint2D origin, double pitch,
        IReadOnlyList<WavefrontNode> rawNodes, IReadOnlyList<WavefrontArc> arcs, WavefrontNumerics numeric)
    {
        var count = footprint.Vertices.Count;
        var order = Enumerable.Range(0, count).Concat(Enumerable.Range(count, rawNodes.Count - count)
            .OrderBy(i => rawNodes[i].Point.X).ThenBy(i => rawNodes[i].Point.Y).ThenBy(i => rawNodes[i].Time)).ToArray();
        var ids = order.Select((old, current) => (old, current)).ToDictionary(p => p.old, p => p.current);
        var local = order.Select(i => rawNodes[i]).ToArray();
        var tangent = Math.Tan(pitch * Math.PI / 180);
        var nodes = local.Select(n => new RoofPoint3D(n.Point.X + origin.X, n.Point.Y + origin.Y, n.Time * tangent)).ToArray();
        var directed = Enumerable.Range(0, count).Select(_ => new List<(int Start, int End)>()).ToArray();
        var edges = new List<RoofTopologyEdge>();
        for (var i = 0; i < count; i++)
        {
            edges.Add(new(i, (i + 1) % count, RoofTopologyEdgeKind.Eave, new[] { i }));
            directed[i].Add((i, (i + 1) % count));
        }
        foreach (var arc in arcs)
        {
            if (!ids.TryGetValue(arc.Start, out var a) || !ids.TryGetValue(arc.End, out var b) ||
                arc.LeftFace < 0 || arc.LeftFace >= count || arc.RightFace < 0 || arc.RightFace >= count ||
                arc.LeftFace == arc.RightFace || a == b) return Unresolved();
            directed[arc.LeftFace].Add((a, b));
            directed[arc.RightFace].Add((b, a));
            edges.Add(new(
                Math.Min(a, b),
                Math.Max(a, b),
                arc.Kind,
                new[] { arc.LeftFace, arc.RightFace },
                arc.Lineage.HasBoundaryCorner ? arc.Lineage.BoundaryVertex : null));
        }
        var faces = new List<RoofTopologyFace>();
        for (var i = 0; i < count; i++)
        {
            var boundary = directed[i];
            if (boundary.Count < 3 || boundary.Select(e => e.Start).Distinct().Count() != boundary.Count ||
                boundary.Select(e => e.End).Distinct().Count() != boundary.Count) return Unresolved();
            var next = boundary.ToDictionary(e => e.Start, e => e.End);
            var cycle = new List<int>();
            var cursor = i;
            do
            {
                if (cycle.Contains(cursor) || !next.TryGetValue(cursor, out var following)) return Unresolved();
                cycle.Add(cursor);
                cursor = following;
            } while (cursor != i);
            if (cycle.Count != boundary.Count || cycle[1] != (i + 1) % count) return Unresolved();
            faces.Add(new(i, cycle));
        }
        edges = edges.Take(count).Concat(edges.Skip(count).OrderBy(e => e.StartNodeIndex).ThenBy(e => e.EndNodeIndex)).ToList();
        if (!RoofTopologyIntegrity.Validate(footprint, local, nodes, edges, faces, tangent, numeric)) return Unresolved();
        var topology = new RoofTopology(nodes, edges, faces, count, pitch);
        if (string.IsNullOrEmpty(topology.Signature)) return Unresolved();
        return new(true, topology, RoofTopologyError.None);
    }

    private static RoofTopologyResult Unresolved() =>
        RoofTopologySolver.Invalid(RoofTopologyError.NumericallyUnresolvedTopology);
}
