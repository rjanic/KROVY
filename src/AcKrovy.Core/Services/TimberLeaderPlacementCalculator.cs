using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberLeaderPlacementCalculator
{
    public const double DefaultTextOffsetMm = 360d;
    public const double PostVerticalGapMm = 120d;

    public static TimberLeaderPlacement CalculateLinear(
        double startX,
        double startY,
        double endX,
        double endY,
        double midpointX,
        double midpointY,
        double textOffsetMm = DefaultTextOffsetMm)
    {
        var text = TimberElementLabelPlacementCalculator.Calculate(
            startX,
            startY,
            endX,
            endY,
            midpointX,
            midpointY,
            textOffsetMm);
        return new TimberLeaderPlacement(
            midpointX,
            midpointY,
            text.X,
            text.Y,
            text.RotationRadians);
    }

    public static TimberLeaderPlacement CalculatePost(
        TimberRectangularFootprintBounds bounds,
        double verticalGapMm = PostVerticalGapMm)
    {
        if (bounds is null)
        {
            throw new ArgumentNullException(nameof(bounds));
        }

        if (verticalGapMm < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(verticalGapMm));
        }

        var centerX = (bounds.MinX + bounds.MaxX) / 2d;
        return new TimberLeaderPlacement(
            centerX,
            bounds.MaxY,
            centerX,
            bounds.MaxY + verticalGapMm + DefaultTextOffsetMm,
            0d);
    }
}
