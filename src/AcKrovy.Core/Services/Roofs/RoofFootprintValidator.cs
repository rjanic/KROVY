using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Validates and canonicalizes one simple polygonal roof footprint.</summary>
public static class RoofFootprintValidator
{
    public const double DuplicateVertexToleranceMm = 0.000000001d;
    public const double ClosingPointToleranceMm = DuplicateVertexToleranceMm;
    public const double MinimumEdgeLengthMm = 0.01d;
    public const double MinimumAreaMm2 = 0.01d;
    public const double CollinearityTolerance = 0.0000000001d;

    public static RoofValidationResult Validate(RoofFootprintInput? input)
    {
        if (input is null)
        {
            return Invalid(RoofValidationError.OpenLoop);
        }

        if (input.HasCurvedSegments)
        {
            return Invalid(RoofValidationError.UnsupportedCurvedSegment);
        }

        if (!input.IsPlanar)
        {
            return Invalid(RoofValidationError.NonPlanar);
        }

        if (input.Vertices is null || input.Vertices.Count < 3)
        {
            return Invalid(RoofValidationError.FewerThanThreeUniqueVertices);
        }

        if (input.Vertices.Any(vertex => !IsFinite(vertex.X) || !IsFinite(vertex.Y)))
        {
            return Invalid(RoofValidationError.NonFiniteCoordinate);
        }

        var vertices = input.Vertices.ToList();
        var hasRepeatedClosingVertex =
            vertices[0].DistanceTo(vertices[vertices.Count - 1]) <= ClosingPointToleranceMm;
        var isEffectiveClosed = input.IsClosed || hasRepeatedClosingVertex;
        if (!isEffectiveClosed)
        {
            return Invalid(RoofValidationError.OpenLoop);
        }

        if (hasRepeatedClosingVertex)
        {
            vertices.RemoveAt(vertices.Count - 1);
        }

        if (CountUniqueVertices(vertices) < 3)
        {
            return Invalid(RoofValidationError.FewerThanThreeUniqueVertices);
        }

        for (var index = 0; index < vertices.Count; index++)
        {
            var length = vertices[index].DistanceTo(vertices[(index + 1) % vertices.Count]);
            if (length <= DuplicateVertexToleranceMm)
            {
                return Invalid(RoofValidationError.DuplicateConsecutiveVertex);
            }

            if (length < MinimumEdgeLengthMm)
            {
                return Invalid(RoofValidationError.ZeroLengthEdge);
            }
        }

        if (HasSelfIntersection(vertices))
        {
            return Invalid(RoofValidationError.SelfIntersection);
        }

        var signedArea = RoofFootprint.CalculateSignedArea(vertices);
        if (Math.Abs(signedArea) < MinimumAreaMm2)
        {
            return Invalid(RoofValidationError.DegenerateArea);
        }

        if (HasRedundantCollinearVertex(vertices))
        {
            return Invalid(RoofValidationError.RedundantCollinearVertex);
        }

        var sourceOrientation = signedArea > 0d
            ? RoofPolygonOrientation.CounterClockwise
            : RoofPolygonOrientation.Clockwise;
        if (sourceOrientation == RoofPolygonOrientation.Clockwise)
        {
            vertices.Reverse();
        }

        var firstIndex = FindCanonicalFirstVertex(vertices);
        var canonical = Enumerable.Range(0, vertices.Count)
            .Select(offset => vertices[(firstIndex + offset) % vertices.Count])
            .ToArray();
        return new RoofValidationResult(
            true,
            new RoofFootprint(canonical),
            RoofValidationError.None,
            sourceOrientation);
    }

    private static RoofValidationResult Invalid(RoofValidationError error) =>
        new(false, null, error, RoofPolygonOrientation.Undefined);

    private static int CountUniqueVertices(IReadOnlyList<RoofPoint2D> vertices)
    {
        var unique = new List<RoofPoint2D>();
        foreach (var vertex in vertices)
        {
            if (unique.All(candidate =>
                    candidate.DistanceTo(vertex) > DuplicateVertexToleranceMm))
            {
                unique.Add(vertex);
            }
        }

        return unique.Count;
    }

    private static bool HasSelfIntersection(IReadOnlyList<RoofPoint2D> vertices)
    {
        for (var first = 0; first < vertices.Count; first++)
        {
            var firstNext = (first + 1) % vertices.Count;
            for (var second = first + 1; second < vertices.Count; second++)
            {
                var secondNext = (second + 1) % vertices.Count;
                if (first == second || firstNext == second || secondNext == first)
                {
                    continue;
                }

                if (SegmentsIntersect(
                        vertices[first],
                        vertices[firstNext],
                        vertices[second],
                        vertices[secondNext]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentsIntersect(
        RoofPoint2D firstStart,
        RoofPoint2D firstEnd,
        RoofPoint2D secondStart,
        RoofPoint2D secondEnd)
    {
        var firstSideStart = Orientation(firstStart, firstEnd, secondStart);
        var firstSideEnd = Orientation(firstStart, firstEnd, secondEnd);
        var secondSideStart = Orientation(secondStart, secondEnd, firstStart);
        var secondSideEnd = Orientation(secondStart, secondEnd, firstEnd);

        if (firstSideStart != firstSideEnd && secondSideStart != secondSideEnd)
        {
            return true;
        }

        return firstSideStart == 0 && IsOnSegment(firstStart, secondStart, firstEnd) ||
               firstSideEnd == 0 && IsOnSegment(firstStart, secondEnd, firstEnd) ||
               secondSideStart == 0 && IsOnSegment(secondStart, firstStart, secondEnd) ||
               secondSideEnd == 0 && IsOnSegment(secondStart, firstEnd, secondEnd);
    }

    private static int Orientation(RoofPoint2D start, RoofPoint2D end, RoofPoint2D point)
    {
        var cross = Cross(start, end, point);
        var scale = Math.Max(
            1d,
            start.DistanceTo(end) * Math.Max(start.DistanceTo(point), end.DistanceTo(point)));
        var tolerance = DuplicateVertexToleranceMm * scale;
        if (Math.Abs(cross) <= tolerance)
        {
            return 0;
        }

        return cross > 0d ? 1 : -1;
    }

    private static bool IsOnSegment(RoofPoint2D start, RoofPoint2D point, RoofPoint2D end) =>
        point.X >= Math.Min(start.X, end.X) - DuplicateVertexToleranceMm &&
        point.X <= Math.Max(start.X, end.X) + DuplicateVertexToleranceMm &&
        point.Y >= Math.Min(start.Y, end.Y) - DuplicateVertexToleranceMm &&
        point.Y <= Math.Max(start.Y, end.Y) + DuplicateVertexToleranceMm;

    private static bool HasRedundantCollinearVertex(IReadOnlyList<RoofPoint2D> vertices)
    {
        for (var index = 0; index < vertices.Count; index++)
        {
            var previous = vertices[(index - 1 + vertices.Count) % vertices.Count];
            var current = vertices[index];
            var next = vertices[(index + 1) % vertices.Count];
            var firstLength = previous.DistanceTo(current);
            var secondLength = current.DistanceTo(next);
            var normalizedCross = Math.Abs(Cross(previous, current, next)) /
                (firstLength * secondLength);
            if (normalizedCross <= CollinearityTolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static double Cross(RoofPoint2D start, RoofPoint2D end, RoofPoint2D point) =>
        (end.X - start.X) * (point.Y - start.Y) -
        (end.Y - start.Y) * (point.X - start.X);

    private static int FindCanonicalFirstVertex(IReadOnlyList<RoofPoint2D> vertices)
    {
        var first = 0;
        for (var index = 1; index < vertices.Count; index++)
        {
            var xDelta = vertices[index].X - vertices[first].X;
            if (xDelta < -DuplicateVertexToleranceMm ||
                Math.Abs(xDelta) <= DuplicateVertexToleranceMm &&
                vertices[index].Y < vertices[first].Y - DuplicateVertexToleranceMm)
            {
                first = index;
            }
        }

        return first;
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
