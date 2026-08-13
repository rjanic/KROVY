using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Solves one centered symmetric gable over a validated rectangular footprint.</summary>
public static class SimpleGableRoofGeometrySolver
{
    public static SimpleGableRoofGeometryResult Solve(RoofDefinition definition)
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

        if (!vertices.All(IsFinite))
        {
            return Invalid(SimpleGableRoofGeometryError.NonFiniteGeometry);
        }

        var edges = Enumerable.Range(0, 4)
            .Select(index => VectorBetween(vertices[index], vertices[(index + 1) % 4]))
            .ToArray();
        var lengths = edges.Select(Length).ToArray();
        if (lengths.Any(length => !IsFinite(length)))
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

        var slopeDegrees = definition.Parameters.SlopeDegrees;
        if (slopeDegrees is not { } slope ||
            !IsFinite(slope) ||
            slope <= SimpleGableRoofGeometryTolerance.MinimumSlopeDegrees ||
            slope >= SimpleGableRoofGeometryTolerance.MaximumSlopeDegrees)
        {
            return Invalid(SimpleGableRoofGeometryError.InvalidSlope);
        }

        if (!TryResolveRidgeAxis(
                definition.Parameters.RidgeDirection,
                edges,
                lengths,
                out var ridgeEdgeIndex,
                out var ridgeDirection))
        {
            return Invalid(SimpleGableRoofGeometryError.RidgeDirectionCannotBeResolved);
        }

        var oppositeRidgeEdgeIndex = (ridgeEdgeIndex + 2) % 4;
        var firstEave = CanonicalSegment(
            vertices[ridgeEdgeIndex],
            vertices[(ridgeEdgeIndex + 1) % 4],
            ridgeDirection);
        var secondEave = CanonicalSegment(
            vertices[oppositeRidgeEdgeIndex],
            vertices[(oppositeRidgeEdgeIndex + 1) % 4],
            ridgeDirection);
        var firstEaveMidpoint = Midpoint(firstEave.Start, firstEave.End);
        var secondEaveMidpoint = Midpoint(secondEave.Start, secondEave.End);
        var transverse = new Vector2(-ridgeDirection.Y, ridgeDirection.X);
        var firstProjection = Dot(firstEaveMidpoint, transverse);
        var secondProjection = Dot(secondEaveMidpoint, transverse);
        var negativeEave = firstProjection <= secondProjection ? firstEave : secondEave;
        var positiveEave = firstProjection <= secondProjection ? secondEave : firstEave;

        var firstGableEdgeIndex = (ridgeEdgeIndex + 1) % 4;
        var secondGableEdgeIndex = (ridgeEdgeIndex + 3) % 4;
        var firstRidgePoint = Midpoint(
            vertices[firstGableEdgeIndex],
            vertices[(firstGableEdgeIndex + 1) % 4]);
        var secondRidgePoint = Midpoint(
            vertices[secondGableEdgeIndex],
            vertices[(secondGableEdgeIndex + 1) % 4]);
        var ridgePlan = CanonicalSegment(firstRidgePoint, secondRidgePoint, ridgeDirection);
        var transverseWidth = firstEaveMidpoint.DistanceTo(secondEaveMidpoint);
        var run = transverseWidth / 2d;
        var rise = run * Math.Tan(slope * Math.PI / 180d);
        if (!IsFinite(run) || !IsFinite(rise) ||
            run <= SimpleGableRoofGeometryTolerance.MinimumDimensionMm / 2d ||
            rise <= 0d)
        {
            return Invalid(SimpleGableRoofGeometryError.NonFiniteGeometry);
        }

        var ridge = new RoofSegment3D(
            AtElevation(ridgePlan.Start, rise),
            AtElevation(ridgePlan.End, rise));
        var negativeEave3D = AtEave(negativeEave);
        var positiveEave3D = AtEave(positiveEave);
        var faces = new[]
        {
            new SimpleGableRoofFace(
                0,
                SimpleGableRoofFaceSide.NegativeTransverse,
                negativeEave3D,
                [ridge.Start, negativeEave3D.Start, negativeEave3D.End, ridge.End]),
            new SimpleGableRoofFace(
                1,
                SimpleGableRoofFaceSide.PositiveTransverse,
                positiveEave3D,
                [ridge.Start, ridge.End, positiveEave3D.End, positiveEave3D.Start]),
        };
        var geometry = new SimpleGableRoofGeometry(
            ridge,
            faces,
            ridgeDirection,
            run,
            rise,
            slope);
        return IsFinite(geometry)
            ? new SimpleGableRoofGeometryResult(true, geometry, SimpleGableRoofGeometryError.None)
            : Invalid(SimpleGableRoofGeometryError.NonFiniteGeometry);
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

    private static bool TryResolveRidgeAxis(
        RoofDirection2D? requestedDirection,
        IReadOnlyList<Vector2> edges,
        IReadOnlyList<double> lengths,
        out int ridgeEdgeIndex,
        out RoofDirection2D ridgeDirection)
    {
        ridgeEdgeIndex = 0;
        ridgeDirection = default;
        if (requestedDirection is not { } requested ||
            !IsFinite(requested.X) ||
            !IsFinite(requested.Y))
        {
            return false;
        }

        var requestedLength = Math.Sqrt(requested.X * requested.X + requested.Y * requested.Y);
        if (!IsFinite(requestedLength) ||
            requestedLength <= SimpleGableRoofGeometryTolerance.AngularTolerance)
        {
            return false;
        }

        var request = new Vector2(requested.X / requestedLength, requested.Y / requestedLength);
        var firstAxis = Scale(edges[0], 1d / lengths[0]);
        var secondAxis = Scale(edges[1], 1d / lengths[1]);
        if (Math.Abs(Cross(request, firstAxis)) <= SimpleGableRoofGeometryTolerance.AngularTolerance)
        {
            ridgeEdgeIndex = 0;
        }
        else if (Math.Abs(Cross(request, secondAxis)) <= SimpleGableRoofGeometryTolerance.AngularTolerance)
        {
            ridgeEdgeIndex = 1;
        }
        else
        {
            return false;
        }

        var oppositeIndex = (ridgeEdgeIndex + 2) % 4;
        var axis = Subtract(
            Scale(edges[ridgeEdgeIndex], 1d / lengths[ridgeEdgeIndex]),
            Scale(edges[oppositeIndex], 1d / lengths[oppositeIndex]));
        var axisLength = Length(axis);
        if (!IsFinite(axisLength) || axisLength <= SimpleGableRoofGeometryTolerance.AngularTolerance)
        {
            return false;
        }

        axis = Scale(axis, 1d / axisLength);
        if (axis.X < 0d || axis.X == 0d && axis.Y < 0d)
        {
            axis = Scale(axis, -1d);
        }

        return RoofDirection2D.TryCreate(axis.X, axis.Y, out ridgeDirection);
    }

    private static Segment2D CanonicalSegment(
        RoofPoint2D first,
        RoofPoint2D second,
        RoofDirection2D direction) =>
        Dot(first, new Vector2(direction.X, direction.Y)) <=
        Dot(second, new Vector2(direction.X, direction.Y))
            ? new Segment2D(first, second)
            : new Segment2D(second, first);

    private static RoofSegment3D AtEave(Segment2D segment) =>
        new(AtElevation(segment.Start, 0d), AtElevation(segment.End, 0d));

    private static RoofPoint3D AtElevation(RoofPoint2D point, double elevation) =>
        new(point.X, point.Y, elevation);

    private static RoofPoint2D Midpoint(RoofPoint2D first, RoofPoint2D second) =>
        new((first.X + second.X) / 2d, (first.Y + second.Y) / 2d);

    private static Vector2 VectorBetween(RoofPoint2D start, RoofPoint2D end) =>
        new(end.X - start.X, end.Y - start.Y);

    private static Vector2 Subtract(Vector2 first, Vector2 second) =>
        new(first.X - second.X, first.Y - second.Y);

    private static Vector2 Scale(Vector2 vector, double scale) =>
        new(vector.X * scale, vector.Y * scale);

    private static double Length(Vector2 vector) =>
        Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);

    private static double Dot(Vector2 first, Vector2 second) =>
        first.X * second.X + first.Y * second.Y;

    private static double Dot(RoofPoint2D point, Vector2 vector) =>
        point.X * vector.X + point.Y * vector.Y;

    private static double Cross(Vector2 first, Vector2 second) =>
        first.X * second.Y - first.Y * second.X;

    private static bool IsFinite(RoofPoint2D point) =>
        IsFinite(point.X) && IsFinite(point.Y);

    private static bool IsFinite(SimpleGableRoofGeometry geometry) =>
        IsFinite(geometry.RunMm) &&
        IsFinite(geometry.RiseMm) &&
        IsFinite(geometry.RidgeLengthMm) &&
        IsFinite(geometry.Ridge.Start) &&
        IsFinite(geometry.Ridge.End) &&
        geometry.Faces.All(face => face.BoundaryPoints.All(IsFinite));

    private static bool IsFinite(RoofPoint3D point) =>
        IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static SimpleGableRoofGeometryResult Invalid(SimpleGableRoofGeometryError error) =>
        new(false, null, error);

    private readonly record struct Vector2(double X, double Y);

    private readonly record struct Segment2D(RoofPoint2D Start, RoofPoint2D End);
}
