using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberSlopeArrowCalculator
{
    public const double AxisLengthMm = 300d;
    public const double HeadLengthMm = 80d;
    public const double HeadHalfWidthMm = 50d;
    public const double OffsetMm = 180d;

    public static bool ShouldDisplay(double slopeDegrees) =>
        !double.IsNaN(slopeDegrees) && !double.IsInfinity(slopeDegrees) && slopeDegrees > 0d;

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
        var uprightDirectionX = sourceDirectionX;
        var uprightDirectionY = sourceDirectionY;
        var sourceRotation = Math.Atan2(sourceDirectionY, sourceDirectionX);
        if (sourceRotation > Math.PI / 2d || sourceRotation <= -Math.PI / 2d)
        {
            uprightDirectionX *= -1d;
            uprightDirectionY *= -1d;
        }

        var labelNormalX = -uprightDirectionY;
        var labelNormalY = uprightDirectionX;
        var directionSign = isReversed ? -1d : 1d;
        var directionX = sourceDirectionX * directionSign;
        var directionY = sourceDirectionY * directionSign;
        var centerX = midpointX - labelNormalX * OffsetMm;
        var centerY = midpointY - labelNormalY * OffsetMm;
        var halfAxisLength = AxisLengthMm / 2d;
        var tailX = centerX - directionX * halfAxisLength;
        var tailY = centerY - directionY * halfAxisLength;
        var tipX = centerX + directionX * halfAxisLength;
        var tipY = centerY + directionY * halfAxisLength;
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
