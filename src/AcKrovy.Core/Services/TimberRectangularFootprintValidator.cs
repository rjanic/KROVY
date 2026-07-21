using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberRectangularFootprintValidator
{
    public const double MinimumEdgeLengthMm = 0.01d;
    public const double MinimumAreaMm2 = 0.01d;
    public const double AngularToleranceDegrees = 0.5d;
    public const double OppositeLengthAbsoluteToleranceMm = 0.01d;
    public const double OppositeLengthRelativeTolerance = 0.001d;

    private static readonly double AngularVectorTolerance =
        Math.Sin(AngularToleranceDegrees * Math.PI / 180d);

    public static TimberRectangularFootprintValidationResult Validate(
        IReadOnlyList<TimberRectangularFootprintPoint>? vertices)
    {
        if (vertices is null || vertices.Count != TimberRectangularFootprintGeometry.RequiredVertexCount)
        {
            return Invalid(TimberRectangularFootprintValidationError.InvalidVertexCount);
        }

        if (vertices.Any(vertex => !IsFinite(vertex.X) || !IsFinite(vertex.Y)))
        {
            return Invalid(TimberRectangularFootprintValidationError.NonFiniteCoordinate);
        }

        var geometry = new TimberRectangularFootprintGeometry(vertices);
        if (geometry.Segments.Any(segment => segment.LengthMm < MinimumEdgeLengthMm))
        {
            return Invalid(TimberRectangularFootprintValidationError.ZeroLengthEdge);
        }

        if (geometry.AreaMm2 < MinimumAreaMm2)
        {
            return Invalid(TimberRectangularFootprintValidationError.DegenerateArea);
        }

        for (var index = 0; index < geometry.Segments.Count; index++)
        {
            var current = geometry.Segments[index];
            var next = geometry.Segments[(index + 1) % geometry.Segments.Count];
            if (Math.Abs(NormalizedDot(current, next)) > AngularVectorTolerance)
            {
                return Invalid(TimberRectangularFootprintValidationError.AdjacentEdgesNotPerpendicular);
            }
        }

        if (Math.Abs(NormalizedCross(geometry.Segments[0], geometry.Segments[2])) > AngularVectorTolerance ||
            Math.Abs(NormalizedCross(geometry.Segments[1], geometry.Segments[3])) > AngularVectorTolerance)
        {
            return Invalid(TimberRectangularFootprintValidationError.OppositeEdgesNotParallel);
        }

        if (!HaveEquivalentLengths(geometry.Segments[0].LengthMm, geometry.Segments[2].LengthMm) ||
            !HaveEquivalentLengths(geometry.Segments[1].LengthMm, geometry.Segments[3].LengthMm))
        {
            return Invalid(TimberRectangularFootprintValidationError.OppositeEdgesDifferentLength);
        }

        return new TimberRectangularFootprintValidationResult(
            true,
            geometry,
            TimberRectangularFootprintValidationError.None);
    }

    public static bool HaveEquivalentLengths(double firstMm, double secondMm)
    {
        if (!IsFinite(firstMm) || !IsFinite(secondMm))
        {
            return false;
        }

        var tolerance = Math.Max(
            OppositeLengthAbsoluteToleranceMm,
            Math.Max(Math.Abs(firstMm), Math.Abs(secondMm)) * OppositeLengthRelativeTolerance);
        return Math.Abs(firstMm - secondMm) <= tolerance;
    }

    private static TimberRectangularFootprintValidationResult Invalid(
        TimberRectangularFootprintValidationError error) => new(false, null, error);

    private static double NormalizedDot(
        TimberRectangularFootprintSegment first,
        TimberRectangularFootprintSegment second)
    {
        var firstX = first.End.X - first.Start.X;
        var firstY = first.End.Y - first.Start.Y;
        var secondX = second.End.X - second.Start.X;
        var secondY = second.End.Y - second.Start.Y;
        return (firstX * secondX + firstY * secondY) / (first.LengthMm * second.LengthMm);
    }

    private static double NormalizedCross(
        TimberRectangularFootprintSegment first,
        TimberRectangularFootprintSegment second)
    {
        var firstX = first.End.X - first.Start.X;
        var firstY = first.End.Y - first.Start.Y;
        var secondX = second.End.X - second.Start.X;
        var secondY = second.End.Y - second.Start.Y;
        return (firstX * secondY - firstY * secondX) / (first.LengthMm * second.LengthMm);
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
