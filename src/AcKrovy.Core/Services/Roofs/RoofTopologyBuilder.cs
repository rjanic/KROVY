using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Assembles immutable connectivity and rejects unresolved/ambiguous geometry.</summary>
internal static class RoofTopologyBuilder
{
    internal static RoofTopologyResult Build(RoofFootprint footprint, RoofPoint2D origin, double pitch,
        IReadOnlyList<IReadOnlyList<RoofPoint2D>> cells, double tolerance, Func<RoofPoint2D, double> heightRun)
    {
        var boundaryCount = footprint.Vertices.Count;
        var candidates = footprint.Vertices.Concat(cells.SelectMany(cell => cell)).ToArray();
        if (candidates.Any(point => !RoofTopologySolver.Finite(point.X) || !RoofTopologySolver.Finite(point.Y)))
        {
            return RoofTopologySolver.Invalid(RoofTopologyError.NonFiniteGeometry);
        }
        var parents = Enumerable.Range(0, candidates.Length).ToArray();
        int Root(int index)
        {
            while (parents[index] != index) index = parents[index];
            return index;
        }
        // Merge simultaneous event positions as a set, never according to face visitation.
        // Reject chain clusters wider than tolerance instead of silently erasing small features.
        var ordered = Enumerable.Range(0, candidates.Length)
            .OrderBy(index => candidates[index].X).ThenBy(index => candidates[index].Y).ToArray();
        for (var i = 0; i < ordered.Length; i++)
        {
            for (var j = i + 1; j < ordered.Length; j++)
            {
                var first = ordered[i];
                var second = ordered[j];
                if (candidates[second].X - candidates[first].X > tolerance) break;
                if (candidates[first].DistanceTo(candidates[second]) <= tolerance)
                {
                    var a = Root(first);
                    var b = Root(second);
                    parents[Math.Max(a, b)] = Math.Min(a, b);
                }
            }
        }
        var clusters = Enumerable.Range(0, candidates.Length).GroupBy(Root).Select(group => group.ToArray()).ToArray();
        var representatives = new Dictionary<int, RoofPoint2D>();
        foreach (var cluster in clusters)
        {
            if (cluster.Count(index => index < boundaryCount) > 1)
            {
                return Unresolved();
            }
            for (var i = 0; i < cluster.Length; i++)
            {
                for (var j = i + 1; j < cluster.Length; j++)
                {
                    if (candidates[cluster[i]].DistanceTo(candidates[cluster[j]]) > tolerance) return Unresolved();
                }
            }
            var key = Root(cluster[0]);
            var anchor = cluster.Where(index => index < boundaryCount).ToArray();
            representatives[key] = anchor.Length == 1 ? candidates[anchor[0]] : new RoofPoint2D(
                cluster.Min(index => candidates[index].X) / 2d + cluster.Max(index => candidates[index].X) / 2d,
                cluster.Min(index => candidates[index].Y) / 2d + cluster.Max(index => candidates[index].Y) / 2d);
        }
        var keys = Enumerable.Range(0, boundaryCount).Select(Root).Concat(
            representatives.Keys.Where(key => key >= boundaryCount)
                .OrderBy(key => representatives[key].X).ThenBy(key => representatives[key].Y)).ToArray();
        var nodeIds = keys.Select((key, index) => (key, index)).ToDictionary(pair => pair.key, pair => pair.index);
        var localNodes = keys.Select(key => representatives[key]).ToArray();
        var tangent = Math.Tan(pitch * Math.PI / 180d);
        var nodes = localNodes.Select((point, index) => new RoofPoint3D(point.X + origin.X, point.Y + origin.Y,
            index < boundaryCount ? 0d : heightRun(point) * tangent)).ToArray();
        if (nodes.Any(point => !RoofTopologySolver.Finite(point.X) || !RoofTopologySolver.Finite(point.Y) ||
                !RoofTopologySolver.Finite(point.Z)) || nodes.Skip(boundaryCount).Any(point => point.Z <= 0d))
        {
            return RoofTopologySolver.Invalid(RoofTopologyError.NonFiniteGeometry);
        }
        var faces = new List<RoofTopologyFace>();
        var offset = boundaryCount;
        for (var i = 0; i < cells.Count; i++)
        {
            var cycle = new List<int>();
            for (var j = 0; j < cells[i].Count; j++)
            {
                var node = nodeIds[Root(offset++)];
                if (cycle.Count == 0 || cycle[cycle.Count - 1] != node) cycle.Add(node);
            }
            if (cycle.Count > 1 && cycle[0] == cycle[cycle.Count - 1]) cycle.RemoveAt(cycle.Count - 1);
            if (cycle.Count < 3 || cycle.Distinct().Count() != cycle.Count) return Unresolved();
            var start = cycle.IndexOf(i);
            if (start < 0 || cycle[(start + 1) % cycle.Count] != (i + 1) % boundaryCount) return Unresolved();
            faces.Add(new RoofTopologyFace(i, Enumerable.Range(0, cycle.Count).Select(j => cycle[(start + j) % cycle.Count])));
        }
        var uses = new Dictionary<(int Start, int End), List<(int Face, bool Forward)>>();
        foreach (var face in faces)
        {
            var cycle = face.BoundaryNodeIndices;
            for (var i = 0; i < cycle.Count; i++)
            {
                var a = cycle[i];
                var b = cycle[(i + 1) % cycle.Count];
                var key = (Math.Min(a, b), Math.Max(a, b));
                if (!uses.TryGetValue(key, out var incidence)) uses[key] = incidence = [];
                incidence.Add((face.SourceEdgeIndex, a < b));
            }
        }
        var edges = new List<RoofTopologyEdge>();
        for (var i = 0; i < boundaryCount; i++)
        {
            var next = (i + 1) % boundaryCount;
            var key = (Math.Min(i, next), Math.Max(i, next));
            if (!uses.TryGetValue(key, out var incidence) || incidence.Count != 1 || incidence[0].Face != i) return Unresolved();
            edges.Add(new RoofTopologyEdge(i, next, RoofTopologyEdgeKind.Eave, [i]));
            uses.Remove(key);
        }
        foreach (var entry in uses.OrderBy(pair => pair.Key.Start).ThenBy(pair => pair.Key.End))
        {
            if (entry.Value.Count != 2 || entry.Value[0].Forward == entry.Value[1].Forward) return Unresolved();
            var (start, end) = entry.Key;
            if (end < boundaryCount) return Unresolved();
            var kind = RoofTopologyEdgeKind.Ridge;
            if (start < boundaryCount)
            {
                var previous = localNodes[(start + boundaryCount - 1) % boundaryCount];
                var current = localNodes[start];
                var next = localNodes[(start + 1) % boundaryCount];
                var turn = Cross(previous, current, next) /
                    (previous.DistanceTo(current) * current.DistanceTo(next));
                if (Math.Abs(turn) <= RoofFootprintValidator.CollinearityTolerance) return Unresolved();
                kind = turn < 0d ? RoofTopologyEdgeKind.Valley : RoofTopologyEdgeKind.Hip;
            }
            edges.Add(new RoofTopologyEdge(start, end, kind, entry.Value.Select(use => use.Face)));
        }
        // A single no-hole roof has a connected tree of skeleton edges, boundary
        // leaves and degree >= 3 internal nodes (including simultaneous events).
        var skeleton = edges.Where(edge => edge.Kind != RoofTopologyEdgeKind.Eave).ToArray();
        if (edges.Any(edge =>
                !RoofTopologySolver.Finite(nodes[edge.StartNodeIndex].DistanceTo(nodes[edge.EndNodeIndex])) ||
                nodes[edge.StartNodeIndex].DistanceTo(nodes[edge.EndNodeIndex]) <= 0d))
        {
            return RoofTopologySolver.Invalid(RoofTopologyError.NonFiniteGeometry);
        }
        if (skeleton.Length != nodes.Length - 1) return Unresolved();
        var adjacency = Enumerable.Range(0, nodes.Length).Select(_ => new List<int>()).ToArray();
        foreach (var edge in skeleton)
        {
            if (localNodes[edge.StartNodeIndex].DistanceTo(localNodes[edge.EndNodeIndex]) <= tolerance) return Unresolved();
            adjacency[edge.StartNodeIndex].Add(edge.EndNodeIndex);
            adjacency[edge.EndNodeIndex].Add(edge.StartNodeIndex);
        }
        if (adjacency.Take(boundaryCount).Any(list => list.Count != 1) ||
            adjacency.Skip(boundaryCount).Any(list => list.Count < 3)) return Unresolved();
        var visited = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(0);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!visited.Add(node)) continue;
            foreach (var neighbor in adjacency[node]) queue.Enqueue(neighbor);
        }
        if (visited.Count != nodes.Length) return Unresolved();

        var area = 0d;
        var heightTolerance = tolerance * Math.Max(1d, tangent);
        foreach (var face in faces)
        {
            var cycle = face.BoundaryNodeIndices;
            var a = localNodes[cycle[0]];
            var b = localNodes[cycle[1]];
            var c = localNodes[cycle[2]];
            var normalZ = Cross(a, b, c);
            if (normalZ <= 0d) return Unresolved();
            var edgeLength = a.DistanceTo(b);
            var faceRun = normalZ / edgeLength;
            var faceTangent = nodes[cycle[2]].Z / faceRun;
            if (!RoofTopologySolver.Finite(faceTangent) || faceTangent <= 0d) return Unresolved();
            foreach (var index in cycle)
            {
                var expectedZ = Cross(a, b, localNodes[index]) / edgeLength * faceTangent;
                if (Math.Abs(expectedZ - nodes[index].Z) > heightTolerance) return Unresolved();
            }
            for (var i = 1; i < cycle.Count - 1; i++)
            {
                var triangleArea = Cross(a, localNodes[cycle[i]], localNodes[cycle[i + 1]]) / 2d;
                if (triangleArea <= 0d) return Unresolved();
                area += triangleArea;
            }
        }
        if (!RoofTopologySolver.Finite(area) ||
            Math.Abs(area - footprint.AreaMm2) > tolerance * footprint.Edges.Sum(edge => edge.LengthMm)) return Unresolved();
        return new RoofTopologyResult(true, new RoofTopology(nodes, edges, faces, boundaryCount, pitch), RoofTopologyError.None);
    }

    private static double Cross(RoofPoint2D a, RoofPoint2D b, RoofPoint2D c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    private static RoofTopologyResult Unresolved() => RoofTopologySolver.Invalid(RoofTopologyError.NumericallyUnresolvedTopology);
}
