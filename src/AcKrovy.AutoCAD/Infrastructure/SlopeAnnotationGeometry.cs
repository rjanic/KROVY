using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class SlopeAnnotationGeometry
{
    public static SlopeAnnotationGeometryData Calculate(Entity sourceEntity)
    {
        ArgumentNullException.ThrowIfNull(sourceEntity);

        return sourceEntity switch
        {
            Line line => new SlopeAnnotationGeometryData(
                line.StartPoint,
                line.EndPoint,
                new Point3d(
                    line.StartPoint.X + (line.EndPoint.X - line.StartPoint.X) *
                        TimberSlopeArrowCalculator.SlopeAnnotationPositionFactor,
                    line.StartPoint.Y + (line.EndPoint.Y - line.StartPoint.Y) *
                        TimberSlopeArrowCalculator.SlopeAnnotationPositionFactor,
                    line.StartPoint.Z + (line.EndPoint.Z - line.StartPoint.Z) *
                        TimberSlopeArrowCalculator.SlopeAnnotationPositionFactor)),
            Polyline polyline => new SlopeAnnotationGeometryData(
                polyline.StartPoint,
                polyline.EndPoint,
                polyline.GetPointAtDist(
                    polyline.Length * TimberSlopeArrowCalculator.SlopeAnnotationPositionFactor)),
            _ => throw new NotSupportedException(
                "Anotáciu sklonu možno vytvoriť iba pre LINE alebo LWPOLYLINE."),
        };
    }
}

internal sealed record SlopeAnnotationGeometryData(
    Point3d Start,
    Point3d End,
    Point3d AnnotationPoint);
