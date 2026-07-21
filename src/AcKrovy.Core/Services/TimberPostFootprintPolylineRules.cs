using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberPostFootprintPolylineRules
{
    public const double GeometricClosureToleranceMm = 0.01d;

    public static bool TryNormalizeVertices(
        bool isClosed,
        IReadOnlyList<TimberRectangularFootprintPoint>? vertices,
        bool hasCurvedSegments,
        bool isSupportedPlanarGeometry,
        out IReadOnlyList<TimberRectangularFootprintPoint> normalizedVertices,
        out TimberPostFootprintPolylineValidationError error)
    {
        normalizedVertices = Array.Empty<TimberRectangularFootprintPoint>();
        if (vertices is null)
        {
            error = TimberPostFootprintPolylineValidationError.WrongSegmentCount;
            return false;
        }

        if (isClosed)
        {
            normalizedVertices = vertices.ToArray();
        }
        else
        {
            if (vertices.Count < 2 || !AreCoincident(vertices[0], vertices[vertices.Count - 1]))
            {
                error = TimberPostFootprintPolylineValidationError.NotClosed;
                return false;
            }

            normalizedVertices = vertices.Take(vertices.Count - 1).ToArray();
        }

        if (normalizedVertices.Count != TimberRectangularFootprintGeometry.RequiredVertexCount)
        {
            error = TimberPostFootprintPolylineValidationError.WrongSegmentCount;
            return false;
        }

        if (hasCurvedSegments)
        {
            error = TimberPostFootprintPolylineValidationError.CurvedSegment;
            return false;
        }

        if (!isSupportedPlanarGeometry)
        {
            error = TimberPostFootprintPolylineValidationError.UnsupportedPlane;
            return false;
        }

        error = TimberPostFootprintPolylineValidationError.None;
        return true;
    }

    private static bool AreCoincident(
        TimberRectangularFootprintPoint first,
        TimberRectangularFootprintPoint second)
    {
        var deltaX = second.X - first.X;
        var deltaY = second.Y - first.Y;
        return deltaX * deltaX + deltaY * deltaY <=
            GeometricClosureToleranceMm * GeometricClosureToleranceMm;
    }
}
