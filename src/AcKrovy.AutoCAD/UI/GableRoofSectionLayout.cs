namespace AcKrovy.AutoCAD.UI;

/// <summary>Uniform model-to-viewport layout for the live technical roof section.</summary>
internal static class GableRoofSectionLayoutCalculator
{
    private const double HorizontalMargin = 96d;
    private const double TopMargin = 74d;
    private const double BottomMargin = 122d;

    public static GableRoofSectionLayout? Create(
        GableRoofSectionState state,
        double viewportWidth,
        double viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight) ||
            viewportWidth < HorizontalMargin * 2d + 20d ||
            viewportHeight < TopMargin + BottomMargin + 20d ||
            !double.IsFinite(state.SpanMm) || state.SpanMm <= 0d)
        {
            return null;
        }

        var minimumElevation = Math.Min(
            state.EaveAElevationMm,
            Math.Min(state.EaveBElevationMm, state.RidgeElevationMm));
        var maximumElevation = Math.Max(
            state.EaveAElevationMm,
            Math.Max(state.EaveBElevationMm, state.RidgeElevationMm));
        var elevationRange = Math.Max(1d, maximumElevation - minimumElevation);
        var availableWidth = viewportWidth - HorizontalMargin * 2d;
        var availableHeight = viewportHeight - TopMargin - BottomMargin;
        var uniformScale = Math.Min(
            availableWidth / state.SpanMm,
            availableHeight / elevationRange);
        if (!double.IsFinite(uniformScale) || uniformScale <= 0d)
        {
            return null;
        }

        var geometryWidth = state.SpanMm * uniformScale;
        var geometryHeight = elevationRange * uniformScale;
        var left = (viewportWidth - geometryWidth) / 2d;
        var top = TopMargin + (availableHeight - geometryHeight) / 2d;

        GableRoofSectionPoint Map(double x, double elevation) => new(
            left + x * uniformScale,
            top + (maximumElevation - elevation) * uniformScale);

        return new GableRoofSectionLayout(
            Map(state.IsMirrored ? 0d : state.SpanMm, state.EaveAElevationMm),
            Map(state.IsMirrored ? state.RunAMm : state.RunBMm, state.RidgeElevationMm),
            Map(state.IsMirrored ? state.SpanMm : 0d, state.EaveBElevationMm),
            Map(0d, 0d).Y,
            uniformScale,
            uniformScale,
            left,
            left + geometryWidth,
            top,
            top + geometryHeight);
    }
}

internal sealed record GableRoofSectionLayout(
    GableRoofSectionPoint EaveA,
    GableRoofSectionPoint Ridge,
    GableRoofSectionPoint EaveB,
    double DatumY,
    double ScaleX,
    double ScaleY,
    double Left,
    double Right,
    double Top,
    double Bottom);

internal readonly record struct GableRoofSectionPoint(double X, double Y);
