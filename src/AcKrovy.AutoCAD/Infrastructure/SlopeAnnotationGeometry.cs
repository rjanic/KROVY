using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class SlopeAnnotationGeometry
{
    public static SlopeAnnotationGeometryData Calculate(Entity sourceEntity, double annotationDistanceMm)
    {
        ArgumentNullException.ThrowIfNull(sourceEntity);

        return sourceEntity switch
        {
            Line line => new SlopeAnnotationGeometryData(
                line.StartPoint,
                line.EndPoint,
                PointOnLine(line, annotationDistanceMm),
                GetPlanarLength(line.StartPoint, line.EndPoint)),
            Polyline polyline => new SlopeAnnotationGeometryData(
                polyline.StartPoint,
                polyline.EndPoint,
                polyline.GetPointAtDist(ClampDistance(annotationDistanceMm, polyline.Length)),
                polyline.Length),
            _ => throw new NotSupportedException(
                UiStrings.ErrorSlopeAnnotationUnsupportedEntityType),
        };
    }

    public static SlopeAnnotationGeometryData CalculatePreferred(Entity sourceEntity)
    {
        var length = sourceEntity switch
        {
            Line line => GetPlanarLength(line.StartPoint, line.EndPoint),
            Polyline polyline => polyline.Length,
            _ => throw new NotSupportedException(
                UiStrings.ErrorSlopeAnnotationUnsupportedEntityType),
        };

        return Calculate(
            sourceEntity,
            length * TimberSlopeArrowCalculator.SlopeAnnotationPositionFactor);
    }

    private static Point3d PointOnLine(Line line, double distance)
    {
        var length = GetPlanarLength(line.StartPoint, line.EndPoint);
        if (length < 0.001d)
        {
            return line.StartPoint;
        }

        var factor = ClampDistance(distance, length) / length;
        return new Point3d(
            line.StartPoint.X + (line.EndPoint.X - line.StartPoint.X) * factor,
            line.StartPoint.Y + (line.EndPoint.Y - line.StartPoint.Y) * factor,
            line.StartPoint.Z + (line.EndPoint.Z - line.StartPoint.Z) * factor);
    }

    private static double ClampDistance(double distance, double length) =>
        Math.Max(0d, Math.Min(length, distance));

    private static double GetPlanarLength(Point3d start, Point3d end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

internal sealed record SlopeAnnotationGeometryData(
    Point3d Start,
    Point3d End,
    Point3d AnnotationPoint,
    double LengthMm);
