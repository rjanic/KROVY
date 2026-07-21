using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberPostFootprintLabelPlacementCalculator
{
    public const double VerticalGapMm = 80d;
    public const double TextHeightMm = 180d;
    public const double LineSpacingFactor = 1d;

    public static TimberPostFootprintLabelPlacement Calculate(
        TimberRectangularFootprintBounds bounds,
        double verticalGapMm = VerticalGapMm)
    {
        if (bounds is null)
        {
            throw new ArgumentNullException(nameof(bounds));
        }
        if (verticalGapMm < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(verticalGapMm));
        }

        return new TimberPostFootprintLabelPlacement(
            AnchorX: (bounds.MinX + bounds.MaxX) / 2d,
            AnchorY: bounds.MaxY + verticalGapMm,
            RotationRadians: 0d);
    }
}
