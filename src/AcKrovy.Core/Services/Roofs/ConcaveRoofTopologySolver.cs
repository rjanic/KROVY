using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

internal static class ConcaveRoofTopologySolver
{
    internal static RoofTopologyResult Solve(RoofFootprint footprint, RoofPoint2D origin, double pitch)
    {
        var sources = footprint.Edges.Select((e, i) => new WavefrontSource(i, e.Start,
            -(e.End.Y - e.Start.Y) / e.LengthMm, (e.End.X - e.Start.X) / e.LengthMm)).ToArray();
        var numeric = new WavefrontNumerics(footprint.Edges.Max(e => e.LengthMm));
        var nodes = footprint.Vertices.Select(p => new WavefrontNode(p, 0)).ToList();
        var arcs = new List<WavefrontArc>();
        var vertices = new List<WavefrontVertex>();
        for (var i = 0; i < sources.Length; i++)
        {
            var vertex = numeric.Vertex(i, (i + sources.Length - 1) % sources.Length, i, i, nodes[i], sources);
            if (vertex is null) return Unresolved();
            vertices.Add(vertex);
        }
        IReadOnlyList<WavefrontLoop> loops = new[] { new WavefrontLoop(0, vertices) };
        var nextVertex = sources.Length;
        var nextLoop = 1;
        var time = 0d;
        // Each valid event consumes original wavefront combinatorial complexity.
        // This generous quadratic guard converts unexpected nonprogress to failure.
        var budget = 4L * sources.Length * sources.Length;
        while (loops.Count > 0)
        {
            if (--budget < 0) return Unresolved();
            var events = WavefrontEvents.Find(loops, sources, time, numeric);
            if (events.Count == 0) return Unresolved();
            var batch = WavefrontEvents.Batch(events, loops, numeric);
            time = batch[0].Time / 2 + batch[batch.Count - 1].Time / 2;
            var resolved = WavefrontEventResolver.Resolve(loops, sources, batch, time, numeric, nodes, arcs,
                ref nextVertex, ref nextLoop);
            if (resolved is null) return Unresolved();
            loops = resolved;
        }
        return WavefrontTopologyAssembler.Build(footprint, origin, pitch, nodes, arcs, numeric);
    }

    private static RoofTopologyResult Unresolved() =>
        RoofTopologySolver.Invalid(RoofTopologyError.NumericallyUnresolvedTopology);
}
