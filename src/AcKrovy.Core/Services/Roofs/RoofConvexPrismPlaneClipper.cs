using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>CAD-neutral clipping of the one ordinary timber prism by end planes.</summary>
public static class RoofConvexPrismPlaneClipper
{
    private const double Tolerance = RoofFaceRafterLayoutService.CoordinateToleranceMm;

    public sealed record Plane(RoofPoint3D Point, RoofPoint3D Normal);
    public sealed record Result(
        IReadOnlyList<RoofPoint3D> Body,
        IReadOnlyList<RoofPoint3D> TopFace,
        IReadOnlyList<RoofPoint3D> BottomFace,
        IReadOnlyList<IReadOnlyList<RoofPoint3D>> CutFaces);

    public static bool TryClip(
        IReadOnlyList<RoofPoint3D> source,
        IReadOnlyList<Plane> planes,
        out Result? result,
        bool allowNoOpPlanes = false)
    {
        result = null;
        if (source.Count != 8 || planes.Count == 0)
        {
            return false;
        }
        int[][] indices =
        [
            [0, 1, 3, 2], [4, 6, 7, 5], [0, 2, 6, 4],
            [1, 5, 7, 3], [0, 4, 5, 1], [2, 3, 7, 6],
        ];
        var faces = indices.Select(face =>
            (IReadOnlyList<RoofPoint3D>)face.Select(index => source[index]).ToArray())
            .ToList();
        foreach (var plane in planes)
        {
            var distances = faces.SelectMany(face => face)
                .Select(point => Distance(point, plane)).ToArray();
            if (distances.Length == 0 || distances.Max() <= Tolerance)
            {
                return false;
            }
            if (distances.Min() >= -Tolerance)
            {
                if (allowNoOpPlanes) continue;
                return false;
            }
            var clipped = faces.Select(face => ClipFace(face, plane)).ToList();
            var onPlane = new List<RoofPoint3D>();
            foreach (var point in clipped.SelectMany(face => face))
            {
                if (Math.Abs(Distance(point, plane)) <= Tolerance)
                {
                    AddUnique(onPlane, point);
                }
            }
            if (!TryOrderCutFace(onPlane, plane.Normal, out var cutFace))
            {
                return false;
            }
            clipped.Add(cutFace);
            faces = clipped;
        }
        var body = new List<RoofPoint3D>();
        foreach (var point in faces.SelectMany(face => face))
        {
            AddUnique(body, point);
        }
        if (body.Count < 4 || faces[0].Count < 3 ||
            body.Any(point => !Finite(point)))
        {
            return false;
        }
        result = new Result(
            Array.AsReadOnly(body.ToArray()),
            Array.AsReadOnly(faces[0].ToArray()),
            Array.AsReadOnly(faces[1].ToArray()),
            Array.AsReadOnly(faces.Skip(6)
                .Select(face => (IReadOnlyList<RoofPoint3D>)Array.AsReadOnly(face.ToArray()))
                .ToArray()));
        return true;
    }

    private static IReadOnlyList<RoofPoint3D> ClipFace(
        IReadOnlyList<RoofPoint3D> polygon, Plane plane)
    {
        var output = new List<RoofPoint3D>();
        if (polygon.Count == 0)
        {
            return output;
        }
        var previous = polygon[polygon.Count - 1];
        var previousDistance = Distance(previous, plane);
        foreach (var current in polygon)
        {
            var currentDistance = Distance(current, plane);
            var previousInside = previousDistance >= -Tolerance;
            var currentInside = currentDistance >= -Tolerance;
            if (previousInside != currentInside)
            {
                var fraction = previousDistance / (previousDistance - currentDistance);
                output.Add(new RoofPoint3D(
                    previous.X + fraction * (current.X - previous.X),
                    previous.Y + fraction * (current.Y - previous.Y),
                    previous.Z + fraction * (current.Z - previous.Z)));
            }
            if (currentInside)
            {
                output.Add(current);
            }
            previous = current;
            previousDistance = currentDistance;
        }
        return output;
    }

    private static bool TryOrderCutFace(
        IReadOnlyList<RoofPoint3D> points,
        RoofPoint3D normal,
        out IReadOnlyList<RoofPoint3D> ordered)
    {
        ordered = Array.Empty<RoofPoint3D>();
        if (points.Count < 3)
        {
            return false;
        }
        var axis = Math.Abs(normal.Z) > 0.9d
            ? new RoofPoint3D(1d, 0d, 0d) : new RoofPoint3D(0d, 0d, 1d);
        var second = Cross(normal, axis);
        var center = new RoofPoint3D(points.Average(p => p.X),
            points.Average(p => p.Y), points.Average(p => p.Z));
        var polygon = points.OrderBy(point =>
            Math.Atan2(Dot(Subtract(point, center), second),
                Dot(Subtract(point, center), axis))).ToArray();
        var area = 0d;
        for (var index = 0; index < polygon.Length; index++)
        {
            area += Dot(normal, Cross(
                Subtract(polygon[index], center),
                Subtract(polygon[(index + 1) % polygon.Length], center)));
        }
        if (!Finite(area) || Math.Abs(area) <= Tolerance)
        {
            return false;
        }
        ordered = Array.AsReadOnly(polygon);
        return true;
    }

    private static double Distance(RoofPoint3D point, Plane plane) =>
        Dot(Subtract(point, plane.Point), plane.Normal);

    private static RoofPoint3D Subtract(RoofPoint3D a, RoofPoint3D b) =>
        new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static RoofPoint3D Cross(RoofPoint3D a, RoofPoint3D b) =>
        new(a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

    private static double Dot(RoofPoint3D a, RoofPoint3D b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static void AddUnique(List<RoofPoint3D> points, RoofPoint3D candidate)
    {
        if (!points.Any(point =>
            Math.Abs(point.X - candidate.X) <= Tolerance &&
            Math.Abs(point.Y - candidate.Y) <= Tolerance &&
            Math.Abs(point.Z - candidate.Z) <= Tolerance))
        {
            points.Add(candidate);
        }
    }

    private static bool Finite(RoofPoint3D point) =>
        Finite(point.X) && Finite(point.Y) && Finite(point.Z);

    private static bool Finite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
