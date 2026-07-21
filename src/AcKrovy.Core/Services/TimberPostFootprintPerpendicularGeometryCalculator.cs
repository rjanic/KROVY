using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberPostFootprintPerpendicularGeometryCalculator
{
    public const double CapLengthMm = 120d;
    public const double StemLengthMm = 100d;
    public const double HorizontalGapMm = 40d;
    public const double TextHeightMm = 120d;
    public const double BottomAnnotationGapMm = 80d;
    public const string DisplayText = "90°";

    private const double SymbolCenterX = -100d;

    public static TimberPostFootprintPerpendicularGeometry CreateLocal()
    {
        var halfCapLength = CapLengthMm / 2d;
        var capStart = new TimberRectangularFootprintPoint(
            SymbolCenterX - halfCapLength,
            -StemLengthMm);
        var capEnd = new TimberRectangularFootprintPoint(
            SymbolCenterX + halfCapLength,
            -StemLengthMm);
        var stemStart = new TimberRectangularFootprintPoint(SymbolCenterX, 0d);
        var stemEnd = new TimberRectangularFootprintPoint(SymbolCenterX, -StemLengthMm);
        var textPosition = new TimberRectangularFootprintPoint(
            capEnd.X + HorizontalGapMm,
            -StemLengthMm / 2d);

        return new TimberPostFootprintPerpendicularGeometry(
            capStart,
            capEnd,
            stemStart,
            stemEnd,
            textPosition,
            DisplayText);
    }

    public static TimberPostFootprintPerpendicularPlacement CalculatePlacement(
        TimberRectangularFootprintBounds bounds,
        double bottomGapMm = BottomAnnotationGapMm)
    {
        if (bounds is null)
        {
            throw new ArgumentNullException(nameof(bounds));
        }

        if (bottomGapMm < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(bottomGapMm));
        }

        return new TimberPostFootprintPerpendicularPlacement(
            AnchorX: (bounds.MinX + bounds.MaxX) / 2d,
            AnchorY: bounds.MinY - bottomGapMm,
            RotationRadians: 0d);
    }
}
