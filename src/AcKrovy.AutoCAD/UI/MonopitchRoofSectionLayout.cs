namespace AcKrovy.AutoCAD.UI;

/// <summary>Uniform model-to-viewport layout for the live Monopitch section.</summary>
internal static class MonopitchRoofSectionLayoutCalculator
{
    private const double HorizontalMargin = 90d;
    private const double TopMargin = 74d;
    private const double BottomMargin = 105d;

    public static MonopitchRoofSectionLayout? Create(
        MonopitchRoofSectionState state,
        double viewportWidth,
        double viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight) ||
            viewportWidth < HorizontalMargin * 2d + 20d ||
            viewportHeight < TopMargin + BottomMargin + 20d ||
            !double.IsFinite(state.SpanMm) || state.SpanMm <= 0d ||
            !double.IsFinite(state.HeightDifferenceMm) || state.HeightDifferenceMm <= 0d)
        {
            return null;
        }

        var availableWidth = viewportWidth - HorizontalMargin * 2d;
        var availableHeight = viewportHeight - TopMargin - BottomMargin;
        var uniformScale = Math.Min(
            availableWidth / state.SpanMm,
            availableHeight / state.HeightDifferenceMm);
        if (!double.IsFinite(uniformScale) || uniformScale <= 0d)
        {
            return null;
        }

        var geometryWidth = state.SpanMm * uniformScale;
        var geometryHeight = state.HeightDifferenceMm * uniformScale;
        var left = (viewportWidth - geometryWidth) / 2d;
        var right = left + geometryWidth;
        var top = TopMargin + (availableHeight - geometryHeight) / 2d;
        var lowY = top + geometryHeight;
        var low = state.IsMirrored
            ? new MonopitchRoofSectionPoint(right, lowY)
            : new MonopitchRoofSectionPoint(left, lowY);
        var high = state.IsMirrored
            ? new MonopitchRoofSectionPoint(left, top)
            : new MonopitchRoofSectionPoint(right, top);
        var projection = new MonopitchRoofSectionPoint(high.X, low.Y);
        var datumY = Math.Min(viewportHeight - 70d, low.Y + 38d);
        var dimensionY = Math.Min(viewportHeight - 32d, datumY + 30d);

        return new MonopitchRoofSectionLayout(
            low,
            high,
            projection,
            datumY,
            dimensionY,
            uniformScale,
            left,
            right,
            top,
            lowY);
    }
}

internal sealed record MonopitchRoofSectionLayout(
    MonopitchRoofSectionPoint Low,
    MonopitchRoofSectionPoint High,
    MonopitchRoofSectionPoint HighProjection,
    double DatumY,
    double DimensionY,
    double Scale,
    double Left,
    double Right,
    double Top,
    double Bottom);

internal readonly record struct MonopitchRoofSectionPoint(double X, double Y);
