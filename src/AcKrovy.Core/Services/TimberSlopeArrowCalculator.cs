using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberSlopeArrowCalculator
{
    public const double SlopeAnnotationPositionFactor = 1d / 3d;
    public const double HeadLengthMm = 120d;
    public const double HeadHalfWidthMm = 50d;

    public static bool ShouldDisplay(double slopeDegrees) =>
        !double.IsNaN(slopeDegrees) && !double.IsInfinity(slopeDegrees) && slopeDegrees > 0d;

    public static TimberSlopeAnnotationPoint CalculatePosition(
        double startX,
        double startY,
        double endX,
        double endY) =>
        new(
            startX + (endX - startX) * SlopeAnnotationPositionFactor,
            startY + (endY - startY) * SlopeAnnotationPositionFactor);

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
        var halfHeadLength = HeadLengthMm / 2d;
        var tipX = midpointX + directionX * halfHeadLength;
        var tipY = midpointY + directionY * halfHeadLength;
        var headBaseX = tipX - directionX * HeadLengthMm;
        var headBaseY = tipY - directionY * HeadLengthMm;
        var headNormalX = -directionY;
        var headNormalY = directionX;

        return new TimberSlopeArrowPlacement(
            tipX,
            tipY,
            headBaseX + headNormalX * HeadHalfWidthMm,
            headBaseY + headNormalY * HeadHalfWidthMm,
            headBaseX - headNormalX * HeadHalfWidthMm,
            headBaseY - headNormalY * HeadHalfWidthMm);
    }
}
