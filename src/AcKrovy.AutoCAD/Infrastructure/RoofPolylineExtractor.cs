using AcKrovy.Core.Models.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Maps an AutoCAD lightweight Polyline into the neutral roof input contract.</summary>
internal static class RoofPolylineExtractor
{
    internal const double BulgeTolerance = 0.000000001d;
    internal const double PlaneNormalTolerance = 0.000001d;

    public static RoofFootprintInput Extract(Polyline polyline)
    {
        ArgumentNullException.ThrowIfNull(polyline);

        var segmentCount = polyline.Closed
            ? polyline.NumberOfVertices
            : Math.Max(0, polyline.NumberOfVertices - 1);
        var hasCurvedSegments = Enumerable.Range(0, segmentCount)
            .Any(index => Math.Abs(polyline.GetBulgeAt(index)) > BulgeTolerance);
        var normal = polyline.Normal;
        var isPlanarInWorldXy = Math.Abs(normal.X) <= PlaneNormalTolerance &&
            Math.Abs(normal.Y) <= PlaneNormalTolerance &&
            Math.Abs(Math.Abs(normal.Z) - 1d) <= PlaneNormalTolerance;
        var vertices = Enumerable.Range(0, polyline.NumberOfVertices)
            .Select(index => polyline.GetPoint3dAt(index))
            .Select(point => new RoofPoint2D(point.X, point.Y))
            .ToArray();

        return new RoofFootprintInput(
            vertices,
            polyline.Closed,
            hasCurvedSegments,
            isPlanarInWorldXy);
    }

    /// <summary>
    /// Returns the WCS elevation used to place neutral local-Z preview geometry.
    /// S1 supports only lightweight polylines whose plane normal is parallel to WCS Z.
    /// </summary>
    public static double GetSourceElevation(Polyline polyline)
    {
        ArgumentNullException.ThrowIfNull(polyline);
        return polyline.NumberOfVertices > 0
            ? polyline.GetPoint3dAt(0).Z
            : 0d;
    }
}
