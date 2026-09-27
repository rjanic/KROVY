using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Shared rectangle recognition for rectangular roof presets. Uses the same
/// tolerances as the stable gable solver so Hip physical-3D eligibility stays aligned.
/// </summary>
public static class RectangularRoofFootprintRules
{
    public static bool TryDescribe(
        RoofFootprint footprint,
        out RectangularRoofFootprintDescription? description)
    {
        description = null;
        if (footprint is null)
        {
            throw new ArgumentNullException(nameof(footprint));
        }

        if (footprint.Vertices.Count != 4)
        {
            return false;
        }

        var vertices = footprint.Vertices;
        if (!vertices.All(IsFinite))
        {
            return false;
        }

        var edges = Enumerable.Range(0, 4)
            .Select(index => VectorBetween(vertices[index], vertices[(index + 1) % 4]))
            .ToArray();
        var lengths = edges.Select(Length).ToArray();
        if (lengths.Any(length => !IsFinite(length)) ||
            lengths.Any(length => length <= SimpleGableRoofGeometryTolerance.MinimumDimensionMm))
        {
            return false;
        }

        if (!IsRectangle(edges, lengths))
        {
            return false;
        }

        var lengthMm = Math.Max(lengths[0], lengths[1]);
        var widthMm = Math.Min(lengths[0], lengths[1]);
        description = new RectangularRoofFootprintDescription(
            LengthMm: lengthMm,
            WidthMm: widthMm,
            HalfWidthMm: widthMm / 2d,
            EdgeLengthsMm: Array.AsReadOnly(lengths));
        return true;
    }

    public static bool IsRectangular(RoofFootprint footprint) =>
        TryDescribe(footprint, out _);

    private static bool IsRectangle(IReadOnlyList<Vector2> edges, IReadOnlyList<double> lengths)
    {
        for (var index = 0; index < 4; index++)
        {
            var next = (index + 1) % 4;
            if (Math.Abs(Dot(edges[index], edges[next]) / (lengths[index] * lengths[next])) >
                SimpleGableRoofGeometryTolerance.AngularTolerance)
            {
                return false;
            }

            if (Cross(edges[index], edges[next]) / (lengths[index] * lengths[next]) <=
                SimpleGableRoofGeometryTolerance.AngularTolerance)
            {
                return false;
            }
        }

        if (Math.Abs(Cross(edges[0], edges[2]) / (lengths[0] * lengths[2])) >
                SimpleGableRoofGeometryTolerance.AngularTolerance ||
            Math.Abs(Cross(edges[1], edges[3]) / (lengths[1] * lengths[3])) >
                SimpleGableRoofGeometryTolerance.AngularTolerance)
        {
            return false;
        }

        return Math.Abs(lengths[0] - lengths[2]) <=
                   SimpleGableRoofGeometryTolerance.LengthTolerance(lengths[0], lengths[2]) &&
               Math.Abs(lengths[1] - lengths[3]) <=
                   SimpleGableRoofGeometryTolerance.LengthTolerance(lengths[1], lengths[3]);
    }

    private static Vector2 VectorBetween(RoofPoint2D start, RoofPoint2D end) =>
        new(end.X - start.X, end.Y - start.Y);

    private static double Length(Vector2 vector) =>
        Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);

    private static double Dot(Vector2 first, Vector2 second) =>
        first.X * second.X + first.Y * second.Y;

    private static double Cross(Vector2 first, Vector2 second) =>
        first.X * second.Y - first.Y * second.X;

    private static bool IsFinite(RoofPoint2D point) =>
        IsFinite(point.X) && IsFinite(point.Y);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private readonly record struct Vector2(double X, double Y);
}

/// <summary>Length = major side, width = minor side of a recognized rectangle.</summary>
public sealed record RectangularRoofFootprintDescription(
    double LengthMm,
    double WidthMm,
    double HalfWidthMm,
    IReadOnlyList<double> EdgeLengthsMm);
