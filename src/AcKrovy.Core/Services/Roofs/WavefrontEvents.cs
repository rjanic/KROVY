using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

internal static class WavefrontEvents
{
    // Recompute from live loops after each batch. There are no stale queue entries.
    internal static IReadOnlyList<WavefrontEvent> Find(IReadOnlyList<WavefrontLoop> loops,
        IReadOnlyList<WavefrontSource> sources, double now, WavefrontNumerics numeric)
    {
        var events = new List<WavefrontEvent>();
        foreach (var loop in loops)
            for (var i = 0; i < loop.Vertices.Count; i++)
            {
                var start = loop.Vertices[i];
                var end = loop.Next(i);
                var source = sources[start.NextSource];
                var length = source.Along(start.At(now), end.At(now));
                var rate = source.Y * (end.Velocity.X - start.Velocity.X) -
                    source.X * (end.Velocity.Y - start.Velocity.Y);
                if (rate < -SimpleGableRoofGeometryTolerance.AngularTolerance)
                {
                    var time = now - length / rate;
                    if (RoofTopologySolver.Finite(time) && time > now + numeric.Arithmetic &&
                        start.At(time).DistanceTo(end.At(time)) <= numeric.Tolerance)
                        events.Add(new(time, WavefrontEventKind.Edge, start.Id, start.Id, end.Id, source.Id));
                }
            }
        foreach (var vertex in loops.SelectMany(loop => loop.Vertices).Where(v => v.Reflex))
            foreach (var loop in loops)
                for (var i = 0; i < loop.Vertices.Count; i++)
                {
                    var start = loop.Vertices[i];
                    var end = loop.Next(i);
                    if (start.Id == vertex.Id || end.Id == vertex.Id) continue;
                    var source = sources[start.NextSource];
                    var rate = source.X * vertex.Velocity.X + source.Y * vertex.Velocity.Y - 1;
                    if (rate >= -SimpleGableRoofGeometryTolerance.AngularTolerance) continue;
                    var time = now - (source.Distance(vertex.At(now)) - now) / rate;
                    if (!RoofTopologySolver.Finite(time) || time <= now + numeric.Arithmetic) continue;
                    var a = start.At(time);
                    var b = end.At(time);
                    if (source.Along(a, b) < -numeric.Tolerance || !numeric.OnSegment(vertex.At(time), a, b)) continue;
                    events.Add(new(time, WavefrontEventKind.Split, vertex.Id, start.Id, end.Id, source.Id));
                }
        return events.OrderBy(e => e.Time).ThenBy(e => e.Kind).ThenBy(e => e.Source)
            .ThenBy(e => e.Vertex).ThenBy(e => e.EdgeStart).ToArray();
    }

    internal static IReadOnlyList<WavefrontEvent> Batch(IReadOnlyList<WavefrontEvent> events,
        IReadOnlyList<WavefrontLoop> loops, WavefrontNumerics numeric)
    {
        // Bound spatial motion, not just clock difference: a shallow acute corner
        // can travel much farther than its supporting front in the same time.
        var speed = loops.SelectMany(l => l.Vertices).Max(v =>
            Math.Sqrt(v.Velocity.X * v.Velocity.X + v.Velocity.Y * v.Velocity.Y));
        var timeTolerance = numeric.Tolerance / Math.Max(1, 2 * speed);
        return events.TakeWhile(e => e.Time - events[0].Time <= timeTolerance).ToArray();
    }
}
