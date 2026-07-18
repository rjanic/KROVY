using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberElementLabelPlacementCalculator
{
    public static TimberElementLabelPlacement Calculate(
        double startX,
        double startY,
        double endX,
        double endY,
        double midpointX,
        double midpointY,
        double offsetMm)
    {
        if (offsetMm < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetMm), "Odstup popisu nesmie byť záporný.");
        }

        var dx = endX - startX;
        var dy = endY - startY;
        var planarLength = Math.Sqrt(dx * dx + dy * dy);
        var rotation = planarLength < 0.001d ? 0d : Math.Atan2(dy, dx);

        if (rotation > Math.PI / 2d || rotation <= -Math.PI / 2d)
        {
            rotation += Math.PI;
        }

        var normalX = -Math.Sin(rotation);
        var normalY = Math.Cos(rotation);

        return new TimberElementLabelPlacement(
            midpointX + normalX * offsetMm,
            midpointY + normalY * offsetMm,
            rotation);
    }
}
