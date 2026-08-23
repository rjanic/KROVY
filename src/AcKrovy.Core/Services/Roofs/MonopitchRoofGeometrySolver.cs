using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Solves one rectangular roof plane from a directed LOW-to-HIGH axis.</summary>
public static class MonopitchRoofGeometrySolver
{
    public static RoofGeometryResult Solve(RoofDefinition definition)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }
        var vertices = definition.Footprint.Vertices;
        if (vertices.Count != 4)
        {
            return Invalid(SimpleGableRoofGeometryError.FootprintIsNotFourSided);
        }

        var edges = Enumerable.Range(0, 4)
            .Select(index => Between(vertices[index], vertices[(index + 1) % 4]))
            .ToArray();
        var lengths = edges.Select(Length).ToArray();
        if (vertices.Any(point => !IsFinite(point.X) || !IsFinite(point.Y)) ||
            lengths.Any(length => !IsFinite(length)))
        {
            return Invalid(SimpleGableRoofGeometryError.NonFiniteGeometry);
        }
        if (lengths.Any(length => length <= SimpleGableRoofGeometryTolerance.MinimumDimensionMm))
        {
            return Invalid(SimpleGableRoofGeometryError.DegenerateDimensions);
        }
        if (!IsRectangle(edges, lengths))
        {
            return Invalid(SimpleGableRoofGeometryError.FootprintIsNotRectangular);
        }
        if (definition.Kind != RoofKind.Monopitch ||
            definition.Parameters.SlopeDirection is not { } requested ||
            !MonopitchRoofMath.IsValidSlope(definition.Parameters.SlopeDegrees ?? double.NaN))
        {
            return Invalid(SimpleGableRoofGeometryError.InvalidSlope);
        }
        if (!TryResolveDirectedAxis(requested, edges, lengths, out var direction))
        {
            return Invalid(SimpleGableRoofGeometryError.RidgeDirectionCannotBeResolved);
        }

        var axis = new Vector2(direction.X, direction.Y);
        var projections = vertices.Select(point => Dot(point, axis)).ToArray();
        var minimum = projections.Min();
        var maximum = projections.Max();
        var span = maximum - minimum;
        if (!MonopitchRoofMath.TryCalculateHeightDifferenceMm(
                span,
                definition.Parameters.SlopeDegrees!.Value,
                out var heightDifference))
        {
            return Invalid(SimpleGableRoofGeometryError.InvalidEaveHeightDifference);
        }

        if (definition.Parameters.EaveHeightDifferenceMm is { } requestedHeight &&
            (!IsFinite(requestedHeight) || requestedHeight <= 0d ||
             Math.Abs(requestedHeight - heightDifference) >
             SimpleGableRoofGeometryTolerance.LengthTolerance(requestedHeight, heightDifference)))
        {
            return Invalid(SimpleGableRoofGeometryError.InvalidEaveHeightDifference);
        }

        var low = vertices
            .Where((_, index) => Math.Abs(projections[index] - minimum) <=
                SimpleGableRoofGeometryTolerance.CoordinateToleranceMm)
            .ToArray();
        var high = vertices
            .Where((_, index) => Math.Abs(projections[index] - maximum) <=
                SimpleGableRoofGeometryTolerance.CoordinateToleranceMm)
            .ToArray();
        if (low.Length != 2 || high.Length != 2)
        {
            return Invalid(SimpleGableRoofGeometryError.RidgeDirectionCannotBeResolved);
        }

        var crossAxis = new Vector2(-axis.Y, axis.X);
        Array.Sort(low, (first, second) => Dot(first, crossAxis).CompareTo(Dot(second, crossAxis)));
        Array.Sort(high, (first, second) => Dot(first, crossAxis).CompareTo(Dot(second, crossAxis)));
        var lowEave = new RoofSegment3D(At(low[0], 0d), At(low[1], 0d));
        var highEave = new RoofSegment3D(At(high[0], heightDifference), At(high[1], heightDifference));
        var geometry = new MonopitchRoofGeometry(
            lowEave,
            highEave,
            [lowEave.Start, lowEave.End, highEave.End, highEave.Start],
            direction,
            span,
            definition.Parameters.SlopeDegrees.Value,
            heightDifference);
        return new RoofGeometryResult(true, geometry, SimpleGableRoofGeometryError.None);
    }

    private static bool TryResolveDirectedAxis(
        RoofDirection2D requested,
        IReadOnlyList<Vector2> edges,
        IReadOnlyList<double> lengths,
        out RoofDirection2D direction)
    {
        direction = default;
        var request = new Vector2(requested.X, requested.Y);
        var first = Scale(edges[0], 1d / lengths[0]);
        var second = Scale(edges[1], 1d / lengths[1]);
        Vector2 selected;
        if (Math.Abs(Cross(request, first)) <= SimpleGableRoofGeometryTolerance.AngularTolerance)
        {
            selected = first;
        }
        else if (Math.Abs(Cross(request, second)) <= SimpleGableRoofGeometryTolerance.AngularTolerance)
        {
            selected = second;
        }
        else
        {
            return false;
        }

        if (Dot(request, selected) < 0d)
        {
            selected = Scale(selected, -1d);
        }
        return RoofDirection2D.TryCreate(selected.X, selected.Y, out direction);
    }

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
        }
        return Math.Abs(lengths[0] - lengths[2]) <=
                   SimpleGableRoofGeometryTolerance.LengthTolerance(lengths[0], lengths[2]) &&
               Math.Abs(lengths[1] - lengths[3]) <=
                   SimpleGableRoofGeometryTolerance.LengthTolerance(lengths[1], lengths[3]);
    }

    private static RoofPoint3D At(RoofPoint2D point, double elevation) =>
        new(point.X, point.Y, elevation);
    private static Vector2 Between(RoofPoint2D start, RoofPoint2D end) =>
        new(end.X - start.X, end.Y - start.Y);
    private static Vector2 Scale(Vector2 value, double scale) =>
        new(value.X * scale, value.Y * scale);
    private static double Length(Vector2 value) => Math.Sqrt(value.X * value.X + value.Y * value.Y);
    private static double Dot(Vector2 first, Vector2 second) => first.X * second.X + first.Y * second.Y;
    private static double Dot(RoofPoint2D point, Vector2 vector) => point.X * vector.X + point.Y * vector.Y;
    private static double Cross(Vector2 first, Vector2 second) => first.X * second.Y - first.Y * second.X;
    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static RoofGeometryResult Invalid(SimpleGableRoofGeometryError error) => new(false, null, error);
    private readonly record struct Vector2(double X, double Y);
}
