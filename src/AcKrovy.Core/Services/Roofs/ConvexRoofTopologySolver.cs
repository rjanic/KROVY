using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Convex straight skeleton by the lower envelope of inward edge-distance planes.
/// d_i(p) is signed distance from source supporting line i. Face i is
/// P intersected with every half-plane d_i(p) &lt;= d_j(p); z = d_i(p) tan(pitch).
/// This is equivalent to equal-speed inward offsets ONLY for convex P.
/// Clipping all constraints avoids arbitrary event-queue ordering at symmetric events.
/// Intended for building footprints, not massive meshes; no large-mesh performance contract.
/// </summary>
internal static class ConvexRoofTopologySolver
{
    internal static RoofTopologyResult Solve(RoofFootprint footprint, RoofPoint2D origin, double pitch)
    {
        var points = footprint.Vertices;
        var planes = footprint.Edges.Select(edge => new Plane(
            -(edge.End.Y - edge.Start.Y) / edge.LengthMm,
            (edge.End.X - edge.Start.X) / edge.LengthMm, edge.Start)).ToArray();
        var scale = footprint.Edges.Max(edge => edge.LengthMm);
        var tolerance = SimpleGableRoofGeometryTolerance.LengthTolerance(scale, scale);
        // Numerical zero for clipping is much smaller than event-coalescence tolerance.
        var arithmeticTolerance = Math.Max(1e-12d, scale * 1e-14d);
        var cells = new List<IReadOnlyList<RoofPoint2D>>();
        for (var i = 0; i < points.Count; i++)
        {
            var cell = points.Select((point, index) => new ClipVertex(point,
                [points.Count + index, points.Count + (index + points.Count - 1) % points.Count])).ToList();
            for (var j = 0; j < points.Count && cell.Count >= 3; j++)
            {
                if (i == j)
                {
                    continue;
                }
                var clipLine = Bisector(planes[i], planes[j]);
                if (clipLine is null)
                {
                    return RoofTopologySolver.Invalid(RoofTopologyError.NumericallyUnresolvedTopology);
                }
                var clipped = new List<ClipVertex>();
                for (var k = 0; k < cell.Count; k++)
                {
                    var start = cell[k];
                    var end = cell[(k + 1) % cell.Count];
                    var first = clipLine.Value.Distance(start.Point);
                    var second = clipLine.Value.Distance(end.Point);
                    if (Math.Abs(first) <= arithmeticTolerance) first = 0d;
                    if (Math.Abs(second) <= arithmeticTolerance) second = 0d;
                    if (first <= 0d) clipped.Add(first == 0d
                        ? start with { Constraints = start.Constraints.Append(j).Distinct().OrderBy(index => index).ToArray() }
                        : start);
                    if (first < 0d && second > 0d || first > 0d && second < 0d)
                    {
                        var constraints = start.Constraints.Intersect(end.Constraints).Append(j).Distinct().OrderBy(index => index).ToArray();
                        var intersection = IntersectConstraints(i, constraints, planes);
                        if (intersection is null)
                        {
                            return RoofTopologySolver.Invalid(RoofTopologyError.NumericallyUnresolvedTopology);
                        }
                        clipped.Add(new ClipVertex(intersection.Value, constraints));
                    }
                }
                cell = clipped;
            }
            if (cell.Count < 3)
            {
                return RoofTopologySolver.Invalid(RoofTopologyError.NumericallyUnresolvedTopology);
            }
            cells.Add(cell.Select(vertex => vertex.Point).ToArray());
        }
        return RoofTopologyBuilder.Build(footprint, origin, pitch, cells, tolerance,
            point => planes.Min(plane => plane.Distance(point)));
    }

    private static RoofPoint2D? IntersectConstraints(int face, int[] constraints, Plane[] planes)
    {
        // A clipped vertex retains the defining constraints of its incident edges.
        // For a three-plane event use all equivalent bisectors and select the best
        // conditioned pair. Interpolating nearly parallel clipped segments would
        // give different event positions on adjacent faces after cancellation.
        var sources = constraints.Where(index => index < planes.Length).Append(face).Distinct().OrderBy(index => index).ToArray();
        var lines = new List<Line>();
        for (var i = 0; i < sources.Length; i++)
        {
            for (var j = i + 1; j < sources.Length; j++)
            {
                if (Bisector(planes[sources[i]], planes[sources[j]]) is { } line) lines.Add(line);
            }
        }
        lines.AddRange(constraints.Where(index => index >= planes.Length).Select(index =>
        {
            var plane = planes[index - planes.Length];
            return new Line(plane.X, plane.Y, plane.Origin, 0d);
        }));
        var best = 0d;
        Line first = default;
        Line second = default;
        for (var i = 0; i < lines.Count; i++)
        {
            for (var j = i + 1; j < lines.Count; j++)
            {
                var determinant = lines[i].X * lines[j].Y - lines[i].Y * lines[j].X;
                if (Math.Abs(determinant) > Math.Abs(best))
                {
                    best = determinant;
                    first = lines[i];
                    second = lines[j];
                }
            }
        }
        if (Math.Abs(best) <= SimpleGableRoofGeometryTolerance.AngularTolerance) return null;
        var origin = planes[sources[0]].Origin;
        var a = -first.Distance(origin);
        var b = -second.Distance(origin);
        return new RoofPoint2D(origin.X + (a * second.Y - b * first.Y) / best,
            origin.Y + (first.X * b - second.X * a) / best);
    }

    private static Line? Bisector(Plane first, Plane second)
    {
        var nx = first.X - second.X;
        var ny = first.Y - second.Y;
        var length = Math.Sqrt(nx * nx + ny * ny);
        if (length <= SimpleGableRoofGeometryTolerance.AngularTolerance) return null;
        // Subtract coefficients before evaluation, not two large similar distances.
        var offset = (second.X * (second.Origin.X - first.Origin.X) +
                      second.Y * (second.Origin.Y - first.Origin.Y)) / length;
        return new Line(nx / length, ny / length, first.Origin, offset);
    }

    private sealed record ClipVertex(RoofPoint2D Point, int[] Constraints);
    private readonly record struct Line(double X, double Y, RoofPoint2D Origin, double Offset)
    {
        internal double Distance(RoofPoint2D point) => X * (point.X - Origin.X) + Y * (point.Y - Origin.Y) + Offset;
    }

    private readonly record struct Plane(double X, double Y, RoofPoint2D Origin)
    {
        internal double Distance(RoofPoint2D point) => X * (point.X - Origin.X) + Y * (point.Y - Origin.Y);
    }
}
