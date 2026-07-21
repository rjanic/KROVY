using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal enum PostFootprintGeometryExtractionError
{
    None = 0,
    NotClosed = 1,
    WrongSegmentCount = 2,
    CurvedSegment = 3,
    UnsupportedPlane = 4,
    InvalidRectangle = 5,
}

internal static class PostFootprintGeometryExtractor
{
    private const double BulgeTolerance = 0.000000001d;
    private const double PlaneNormalTolerance = 0.000001d;

    public static bool TryExtract(
        Polyline polyline,
        out TimberRectangularFootprintGeometry? geometry,
        out PostFootprintGeometryExtractionError error)
    {
        ArgumentNullException.ThrowIfNull(polyline);
        geometry = null;

        var normal = polyline.Normal;
        var segmentCount = polyline.Closed
            ? polyline.NumberOfVertices
            : Math.Max(0, polyline.NumberOfVertices - 1);
        var hasCurvedSegments = Enumerable.Range(0, segmentCount)
            .Any(index => Math.Abs(polyline.GetBulgeAt(index)) > BulgeTolerance);
        var isSupportedPlane = Math.Abs(normal.X) <= PlaneNormalTolerance &&
            Math.Abs(normal.Y) <= PlaneNormalTolerance &&
            Math.Abs(Math.Abs(normal.Z) - 1d) <= PlaneNormalTolerance;
        var rawVertices = Enumerable.Range(0, polyline.NumberOfVertices)
            .Select(index => polyline.GetPoint3dAt(index))
            .Select(point => new TimberRectangularFootprintPoint(point.X, point.Y))
            .ToArray();
        if (!TimberPostFootprintPolylineRules.TryNormalizeVertices(
                polyline.Closed,
                rawVertices,
                hasCurvedSegments,
                isSupportedPlane,
                out var normalizedVertices,
                out var structureError))
        {
            error = structureError switch
            {
                TimberPostFootprintPolylineValidationError.NotClosed => PostFootprintGeometryExtractionError.NotClosed,
                TimberPostFootprintPolylineValidationError.WrongSegmentCount => PostFootprintGeometryExtractionError.WrongSegmentCount,
                TimberPostFootprintPolylineValidationError.CurvedSegment => PostFootprintGeometryExtractionError.CurvedSegment,
                _ => PostFootprintGeometryExtractionError.UnsupportedPlane,
            };
            return false;
        }

        var validation = TimberRectangularFootprintValidator.Validate(normalizedVertices);
        if (!validation.IsValid || validation.Geometry is null)
        {
            error = PostFootprintGeometryExtractionError.InvalidRectangle;
            return false;
        }

        geometry = validation.Geometry;
        error = PostFootprintGeometryExtractionError.None;
        return true;
    }
}
