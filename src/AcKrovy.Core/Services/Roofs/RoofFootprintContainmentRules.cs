using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// CAD-neutral containment tests for roof-footprint polygons in the drawing XY plane.
/// Boundary counts as inside. Used by the AttachedManual keep/delete policy after a
/// non-rigid source SupportedResize: the entire segment must lie inside or on the
/// final roof footprint within the existing geometry tolerance.
/// </summary>
public static class RoofFootprintContainmentRules
{
    /// <summary>
    /// Reuses the existing roof geometry coordinate tolerance. A point within this
    /// distance of the boundary is treated as on-boundary (inside).
    /// </summary>
    public const double ContainmentToleranceMm = SimpleGableRoofGeometryTolerance.CoordinateToleranceMm;

    /// <summary>
    /// True when the point lies strictly inside the polygon or on its boundary within
    /// <see cref="ContainmentToleranceMm"/>. Vertices are a closed polygon; the final
    /// edge implicitly closes back to vertex zero.
    /// </summary>
    public static bool IsPointInsideOrOnBoundary(
        RoofPoint2D point,
        IReadOnlyList<RoofPoint2D> vertices)
    {
        if (vertices is null || vertices.Count < 3)
        {
            return false;
        }

        if (IsOnBoundary(point, vertices, ContainmentToleranceMm))
        {
            return true;
        }

        // Even-odd ray casting. The boundary pre-check above already resolved
        // on-edge points, so this runs only for strictly interior/exterior points.
        var inside = false;
        for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
        {
            var vi = vertices[i];
            var vj = vertices[j];
            if (vi.Y > point.Y != vj.Y > point.Y)
            {
                var xIntersect = (vj.X - vi.X) * (point.Y - vi.Y) / (vj.Y - vi.Y) + vi.X;
                if (point.X < xIntersect)
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }

    /// <summary>
    /// True when the entire segment lies inside or on the polygon boundary. Both
    /// endpoints must be inside/on and the segment must not properly cross any edge
    /// (which would leave a portion outside a non-convex polygon).
    /// </summary>
    public static bool IsSegmentInsideOrOnBoundary(
        RoofPoint2D start,
        RoofPoint2D end,
        IReadOnlyList<RoofPoint2D> vertices)
    {
        if (vertices is null || vertices.Count < 3)
        {
            return false;
        }

        if (!IsPointInsideOrOnBoundary(start, vertices) ||
            !IsPointInsideOrOnBoundary(end, vertices))
        {
            return false;
        }

        for (var index = 0; index < vertices.Count; index++)
        {
            var edgeStart = vertices[index];
            var edgeEnd = vertices[(index + 1) % vertices.Count];
            if (SegmentsProperlyCross(start, end, edgeStart, edgeEnd))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsOnBoundary(
        RoofPoint2D point,
        IReadOnlyList<RoofPoint2D> vertices,
        double tolerance)
    {
        for (var index = 0; index < vertices.Count; index++)
        {
            if (DistanceToSegment(point, vertices[index], vertices[(index + 1) % vertices.Count]) <=
                tolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static double DistanceToSegment(RoofPoint2D point, RoofPoint2D start, RoofPoint2D end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0d)
        {
            return point.DistanceTo(start);
        }

        var projection = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared;
        projection = Math.Max(0d, Math.Min(1d, projection));
        var closest = new RoofPoint2D(start.X + projection * dx, start.Y + projection * dy);
        return point.DistanceTo(closest);
    }

    /// <summary>
    /// Strict (proper) crossing: both endpoint pairs straddle each other with no
    /// collinear/touching orientation. Touching or collinear contact is boundary
    /// contact, not a crossing that leaves the polygon.
    /// </summary>
    private static bool SegmentsProperlyCross(
        RoofPoint2D firstStart,
        RoofPoint2D firstEnd,
        RoofPoint2D secondStart,
        RoofPoint2D secondEnd)
    {
        var firstStartSide = Orientation(firstStart, firstEnd, secondStart);
        var firstEndSide = Orientation(firstStart, firstEnd, secondEnd);
        var secondStartSide = Orientation(secondStart, secondEnd, firstStart);
        var secondEndSide = Orientation(secondStart, secondEnd, firstEnd);
        return firstStartSide != 0 &&
               firstEndSide != 0 &&
               secondStartSide != 0 &&
               secondEndSide != 0 &&
               firstStartSide != firstEndSide &&
               secondStartSide != secondEndSide;
    }

    private static int Orientation(RoofPoint2D start, RoofPoint2D end, RoofPoint2D point)
    {
        var cross = Cross(start, end, point);
        var scale = Math.Max(
            1d,
            start.DistanceTo(end) * Math.Max(start.DistanceTo(point), end.DistanceTo(point)));
        var tolerance = ContainmentToleranceMm * scale;
        if (Math.Abs(cross) <= tolerance)
        {
            return 0;
        }

        return cross > 0d ? 1 : -1;
    }

    private static double Cross(RoofPoint2D start, RoofPoint2D end, RoofPoint2D point) =>
        (end.X - start.X) * (point.Y - start.Y) -
        (end.Y - start.Y) * (point.X - start.X);
}
