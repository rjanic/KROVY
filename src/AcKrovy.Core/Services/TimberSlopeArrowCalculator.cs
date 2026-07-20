using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberSlopeArrowCalculator
{
    public const double SlopeAnnotationPositionFactor = 1d / 3d;
    public const double SlopeArrowPositionFactor = 1d / 2d;
    public const double AxisLengthMm = 180d;
    public const double HeadLengthMm = 65d;
    public const double HeadHalfWidthMm = 28d;

    public static bool ShouldDisplay(double slopeDegrees) =>
        TimberSlopeAnnotationRules.ResolveGlyphKind(slopeDegrees) == TimberSlopeGlyphKind.DirectionalArrow;

    public static bool ShouldDisplayHorizontalMarker(double slopeDegrees) =>
        TimberSlopeAnnotationRules.ResolveGlyphKind(slopeDegrees) == TimberSlopeGlyphKind.HorizontalMarker;

    public static TimberSlopeAnnotationPoint CalculatePosition(
        double startX,
        double startY,
        double endX,
        double endY) =>
        new(
            startX + (endX - startX) * SlopeArrowPositionFactor,
            startY + (endY - startY) * SlopeArrowPositionFactor);

    public static TimberSlopeArrowPlacement Calculate(
        double startX,
        double startY,
        double endX,
        double endY,
        double midpointX,
        double midpointY,
        bool isReversed)
    {
        var dx = endX - startX;
        var dy = endY - startY;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.001d)
        {
            throw new ArgumentException("Smerovú šípku nemožno vypočítať pre nulovú pôdorysnú dĺžku.");
        }

        var sourceDirectionX = dx / length;
        var sourceDirectionY = dy / length;
        var directionSign = isReversed ? -1d : 1d;
        var directionX = sourceDirectionX * directionSign;
        var directionY = sourceDirectionY * directionSign;
        var halfAxisLength = AxisLengthMm / 2d;
        var tailX = midpointX - directionX * halfAxisLength;
        var tailY = midpointY - directionY * halfAxisLength;
        var tipX = midpointX + directionX * halfAxisLength;
        var tipY = midpointY + directionY * halfAxisLength;
        var headBaseX = tipX - directionX * HeadLengthMm;
        var headBaseY = tipY - directionY * HeadLengthMm;
        var headNormalX = -directionY;
        var headNormalY = directionX;

        return new TimberSlopeArrowPlacement(
            tailX,
            tailY,
            tipX,
            tipY,
            headBaseX + headNormalX * HeadHalfWidthMm,
            headBaseY + headNormalY * HeadHalfWidthMm,
            headBaseX - headNormalX * HeadHalfWidthMm,
            headBaseY - headNormalY * HeadHalfWidthMm);
    }
}
