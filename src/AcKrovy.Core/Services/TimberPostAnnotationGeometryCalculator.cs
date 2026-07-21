using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// Builds the presentation-only inverted-T symbol for a vertical Post. All
/// dimensions are drawing millimetres and remain independent of AutoCAD types.
/// </summary>
public static class TimberPostAnnotationGeometryCalculator
{
    public const double CapHalfLengthMm = 60d;
    public const double StemLengthMm = 100d;
    public const double AnnotationNormalOffsetMm = 30d;
    public const double TextLongitudinalOffsetMm = 160d;
    public const double TextNormalOffsetMm = 50d;
    public const double CollisionHalfExtentMm = 270d;
    public const double DisplayAngleDegrees = 90d;

    /// <summary>Geometry stored in the block definition, centred at its local origin.</summary>
    public static TimberPostAnnotationGeometry CreateLocal() => new(
        new TimberSlopeAnnotationPoint(0d, 0d),
        new TimberSlopeAnnotationPoint(-CapHalfLengthMm, 0d),
        new TimberSlopeAnnotationPoint(CapHalfLengthMm, 0d),
        new TimberSlopeAnnotationPoint(0d, StemLengthMm),
        new TimberSlopeAnnotationPoint(TextLongitudinalOffsetMm, TextNormalOffsetMm),
        0d);

    public static TimberPostAnnotationGeometry Calculate(
        double startX,
        double startY,
        double endX,
        double endY,
        double anchorX,
        double anchorY)
    {
        var readablePlacement = TimberElementLabelPlacementCalculator.Calculate(
            startX,
            startY,
            endX,
            endY,
            anchorX,
            anchorY,
            0d);
        var rotation = readablePlacement.RotationRadians;
        var normalX = -Math.Sin(rotation);
        var normalY = Math.Cos(rotation);
        var groupAnchorX = anchorX + normalX * AnnotationNormalOffsetMm;
        var groupAnchorY = anchorY + normalY * AnnotationNormalOffsetMm;
        return TransformLocalToWorld(
            CreateLocal(),
            groupAnchorX,
            groupAnchorY,
            rotation);
    }

    public static TimberPostAnnotationGeometry TransformLocalToWorld(
        TimberPostAnnotationGeometry local,
        double anchorX,
        double anchorY,
        double rotationRadians)
    {
        if (local is null)
        {
            throw new ArgumentNullException(nameof(local));
        }

        return new TimberPostAnnotationGeometry(
            Transform(local.Anchor, anchorX, anchorY, rotationRadians),
            Transform(local.CapStart, anchorX, anchorY, rotationRadians),
            Transform(local.CapEnd, anchorX, anchorY, rotationRadians),
            Transform(local.StemEnd, anchorX, anchorY, rotationRadians),
            Transform(local.TextPosition, anchorX, anchorY, rotationRadians),
            rotationRadians);
    }

    private static TimberSlopeAnnotationPoint Transform(
        TimberSlopeAnnotationPoint local,
        double anchorX,
        double anchorY,
        double rotationRadians)
    {
        var cosine = Math.Cos(rotationRadians);
        var sine = Math.Sin(rotationRadians);
        return new TimberSlopeAnnotationPoint(
            anchorX + local.X * cosine - local.Y * sine,
            anchorY + local.X * sine + local.Y * cosine);
    }
}
