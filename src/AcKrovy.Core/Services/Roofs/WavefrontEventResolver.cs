using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Resolve the complete planar incidence graph at one event time.</summary>
internal static class WavefrontEventResolver
{
    internal static IReadOnlyList<WavefrontLoop>? Resolve(IReadOnlyList<WavefrontLoop> loops,
        IReadOnlyList<WavefrontSource> sources, IReadOnlyList<WavefrontEvent> batch, double time, WavefrontNumerics numeric,
        List<WavefrontNode> nodes, List<WavefrontArc> arcs, ref int nextVertex, ref int nextLoop)
    {
        var vertices = loops.SelectMany(l => l.Vertices).ToArray();
        var points = vertices.Select(v => v.At(time)).ToArray();
        var roots = numeric.Cluster(points);
        if (roots is null) return null;
        var members = Enumerable.Range(0, vertices.Length).GroupBy(i => roots[i])
            .ToDictionary(g => g.Key, g => g.ToArray());
        var positions = members.ToDictionary(g => g.Key, g => new RoofPoint2D(
            g.Value.Min(i => points[i].X) / 2 + g.Value.Max(i => points[i].X) / 2,
            g.Value.Min(i => points[i].Y) / 2 + g.Value.Max(i => points[i].Y) / 2));
        var vertexRoots = vertices.Select((v, i) => (v.Id, Root: roots[i])).ToDictionary(p => p.Id, p => p.Root);
        var touched = new HashSet<int>(members.Where(g => g.Value.Length > 1).Select(g => g.Key));
        var spans = new List<WavefrontSpan>();
        foreach (var loop in loops)
            for (var i = 0; i < loop.Vertices.Count; i++)
            {
                var vertex = loop.Vertices[i];
                var a = vertexRoots[vertex.Id];
                var b = vertexRoots[loop.Next(i).Id];
                if (a == b) continue;
                var source = sources[vertex.NextSource];
                if (source.Along(positions[a], positions[b]) <= numeric.Tolerance) return null;
                var cuts = positions.Keys.Where(k => numeric.OnSegment(positions[k], positions[a], positions[b]))
                    .OrderBy(k => source.Along(positions[a], positions[k])).ThenBy(k => k).ToArray();
                if (cuts[0] != a || cuts[cuts.Length - 1] != b) return null;
                foreach (var cut in cuts.Where(k => k != a && k != b)) touched.Add(cut);
                for (var j = 0; j + 1 < cuts.Length; j++) spans.Add(new(cuts[j], cuts[j + 1], source.Id));
            }

        var remaining = new List<WavefrontSpan>();
        var terminal = new List<(WavefrontSpan First, WavefrontSpan Second)>();
        foreach (var group in spans.GroupBy(s => (Math.Min(s.Start, s.End), Math.Max(s.Start, s.End))))
        {
            var entries = group.ToArray();
            if (entries.Length == 1) remaining.Add(entries[0]);
            else
            {
                if (entries.Length != 2 || entries[0].Start != entries[1].End ||
                    entries[0].Source == entries[1].Source) return null;
                // A zero-width strip disappears as a whole, leaving a ridge.
                // This includes positive-length simultaneous orthogonal collapses.
                terminal.Add((entries[0], entries[1]));
                touched.Add(entries[0].Start);
                touched.Add(entries[0].End);
            }
        }
        if (touched.Count == 0) return null;
        // Every queued event in this batch must be represented by the contact
        // graph. An inconsistent near-time candidate cannot just be invalidated.
        foreach (var pending in batch)
        {
            var contact = vertexRoots[pending.Vertex];
            if (!touched.Contains(contact) || !numeric.OnSegment(positions[contact],
                positions[vertexRoots[pending.EdgeStart]], positions[vertexRoots[pending.EdgeEnd]])) return null;
        }
        var eventNodes = new Dictionary<int, int>();
        foreach (var root in touched.OrderBy(k => positions[k].X).ThenBy(k => positions[k].Y))
        {
            eventNodes[root] = nodes.Count;
            nodes.Add(new(positions[root], time));
            foreach (var index in members[root])
            {
                var vertex = vertices[index];
                if (nodes[vertex.Anchor].Point.DistanceTo(positions[root]) <= numeric.Tolerance) return null;
                var kind = vertex.Coplanar ? RoofTopologyEdgeKind.CoplanarSeam :
                    vertex.Reflex ? RoofTopologyEdgeKind.Valley :
                    vertex.Lineage.Kind == WavefrontBoundaryCornerKind.Convex
                        ? RoofTopologyEdgeKind.Hip
                        : RoofTopologyEdgeKind.Ridge;
                arcs.Add(new(
                    vertex.Anchor,
                    eventNodes[root],
                    vertex.PreviousSource,
                    vertex.NextSource,
                    kind,
                    vertex.Lineage));
            }
        }
        foreach (var pair in terminal)
            arcs.Add(new(eventNodes[pair.First.Start], eventNodes[pair.First.End],
                pair.Second.Source, pair.First.Source, RoofTopologyEdgeKind.Ridge,
                WavefrontLineage.Internal));

        if (remaining.Count == 0) return Array.Empty<WavefrontLoop>();
        // Follow the left-hand bounded region: at a multi-contact node choose the
        // outgoing ray immediately clockwise from the reversed incoming ray.
        // Pair the entire star and require a bijection; never discard a tied event.
        var successor = new Dictionary<int, int>();
        for (var i = 0; i < remaining.Count; i++)
        {
            var incoming = remaining[i];
            var a = sources[incoming.Source];
            var candidates = Enumerable.Range(0, remaining.Count).Where(j => remaining[j].Start == incoming.End)
                .Select(j =>
                {
                    var b = sources[remaining[j].Source];
                    var angle = -Math.Atan2(a.Y * b.X - a.X * b.Y, -a.X * b.X - a.Y * b.Y);
                    if (angle < 0) angle += 2 * Math.PI;
                    return (Index: j, Angle: angle);
                }).OrderBy(p => p.Angle).ThenBy(p => remaining[p.Index].Source).ToArray();
            if (candidates.Length == 0 || candidates[0].Angle <= SimpleGableRoofGeometryTolerance.AngularTolerance ||
                candidates.Length > 1 && candidates[1].Angle - candidates[0].Angle <= SimpleGableRoofGeometryTolerance.AngularTolerance)
                return null;
            successor[i] = candidates[0].Index;
        }
        if (successor.Values.Distinct().Count() != remaining.Count) return null;
        var successorCounts = successor.Select(pair => remaining[pair.Key].End)
            .Where(touched.Contains)
            .GroupBy(root => root)
            .ToDictionary(group => group.Key, group => group.Count());
        var newVertices = new Dictionary<int, WavefrontVertex>();
        foreach (var pair in successor.OrderBy(p => p.Value))
        {
            var incoming = remaining[pair.Key];
            var outgoing = remaining[pair.Value];
            var root = incoming.End;
            WavefrontVertex? vertex;
            if (touched.Contains(root))
            {
                var anchor = eventNodes[root];
                vertex = numeric.Vertex(nextVertex++, incoming.Source, outgoing.Source, anchor, nodes[anchor], sources,
                    WavefrontLineage.Internal);
                if (vertex is not null)
                {
                    var terminalRoots = terminal.Select(pair =>
                        pair.First.Start == root ? pair.First.End :
                        pair.First.End == root ? pair.First.Start : -1).Where(candidate => candidate >= 0).Distinct();
                    var lineage = WavefrontLineage.ForSuccessor(
                        members[root].Select(index => vertices[index]),
                        terminalRoots.SelectMany(candidate => members[candidate]).Select(index => vertices[index]),
                        successorCounts[root],
                        incoming.Source,
                        outgoing.Source,
                        vertex.Reflex,
                        vertex.Coplanar);
                    vertex = vertex with { Lineage = lineage };
                }
            }
            else
            {
                vertex = vertices[members[root][0]];
                if (vertex.PreviousSource != incoming.Source || vertex.NextSource != outgoing.Source) return null;
            }
            if (vertex is null) return null;
            newVertices[pair.Value] = vertex;
        }
        var result = new List<WavefrontLoop>();
        var visited = new HashSet<int>();
        for (var start = 0; start < remaining.Count; start++)
        {
            if (visited.Contains(start)) continue;
            var cycle = new List<WavefrontVertex>();
            var cursor = start;
            do
            {
                if (!visited.Add(cursor)) return null;
                cycle.Add(newVertices[cursor]);
                cursor = successor[cursor];
            } while (cursor != start);
            if (cycle.Count < 3) return null;
            var area = Enumerable.Range(1, cycle.Count - 2).Sum(i => WavefrontNumerics.Cross(
                cycle[0].At(time), cycle[i].At(time), cycle[i + 1].At(time))) / 2;
            if (!RoofTopologySolver.Finite(area) || area <= numeric.Tolerance * numeric.Tolerance) return null;
            result.Add(new(nextLoop++, cycle));
        }
        return result;
    }
}
