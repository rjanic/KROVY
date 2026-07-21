using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberPolylineSegmentPickResolver
{
    public const double MaximumPickDistanceMm = 50d;
    public const double AmbiguityDistanceToleranceMm = 1d;

    public static TimberPolylineSegmentPickResult Resolve(
        TimberRectangularFootprintGeometry geometry,
        TimberRectangularFootprintPoint pickedPoint)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (pickedPoint is null)
        {
            throw new ArgumentNullException(nameof(pickedPoint));
        }

        if (!TimberRectangularFootprintValidator.Validate(geometry.Vertices).IsValid)
        {
            return new TimberPolylineSegmentPickResult(
                TimberPolylineSegmentPickStatus.InvalidGeometry,
                null,
                double.PositiveInfinity);
        }

        var candidates = geometry.Segments
            .Select(segment => new
            {
                segment.Index,
                DistanceMm = DistanceToSegment(pickedPoint, segment),
            })
            .OrderBy(candidate => candidate.DistanceMm)
            .ThenBy(candidate => candidate.Index)
            .ToList();
        var nearest = candidates[0];

        if (nearest.DistanceMm > MaximumPickDistanceMm)
        {
            return new TimberPolylineSegmentPickResult(
                TimberPolylineSegmentPickStatus.TooFar,
                null,
                nearest.DistanceMm);
        }

        if (candidates.Count > 1 &&
            Math.Abs(candidates[1].DistanceMm - nearest.DistanceMm) <= AmbiguityDistanceToleranceMm)
        {
            return new TimberPolylineSegmentPickResult(
                TimberPolylineSegmentPickStatus.Ambiguous,
                null,
                nearest.DistanceMm);
        }

        return new TimberPolylineSegmentPickResult(
            TimberPolylineSegmentPickStatus.Success,
            nearest.Index,
            nearest.DistanceMm);
    }

    private static double DistanceToSegment(
        TimberRectangularFootprintPoint point,
        TimberRectangularFootprintSegment segment)
    {
        var dx = segment.End.X - segment.Start.X;
        var dy = segment.End.Y - segment.Start.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0d)
        {
            var pointDx = point.X - segment.Start.X;
            var pointDy = point.Y - segment.Start.Y;
            return Math.Sqrt(pointDx * pointDx + pointDy * pointDy);
        }

        var projection = ((point.X - segment.Start.X) * dx +
            (point.Y - segment.Start.Y) * dy) / lengthSquared;
        var clamped = Math.Max(0d, Math.Min(1d, projection));
        var closestX = segment.Start.X + clamped * dx;
        var closestY = segment.Start.Y + clamped * dy;
        var distanceX = point.X - closestX;
        var distanceY = point.Y - closestY;
        return Math.Sqrt(distanceX * distanceX + distanceY * distanceY);
    }
}
