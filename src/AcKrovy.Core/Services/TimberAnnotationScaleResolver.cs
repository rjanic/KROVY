using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberAnnotationScaleResolver
{
    public static TimberAnnotationScaleContext ResolveDrawingContext(
        bool hasDrawingValue,
        int drawingDenominator)
    {
        var hasValidDrawingValue =
            hasDrawingValue &&
            TimberAnnotationScaleRules.IsValidDenominator(drawingDenominator);

        return new TimberAnnotationScaleContext(
            hasValidDrawingValue
                ? drawingDenominator
                : TimberAnnotationScaleRules.DefaultDenominator,
            hasValidDrawingValue
                ? TimberAnnotationScaleSource.Drawing
                : TimberAnnotationScaleSource.FixedDefault);
    }

    public static TimberAnnotationScaleContext ResolveElementContext(
        TimberAnnotationScaleContext drawingContext,
        int? elementOverride)
    {
        if (drawingContext is null)
        {
            throw new ArgumentNullException(nameof(drawingContext));
        }

        return elementOverride.HasValue &&
               TimberAnnotationScaleRules.IsValidDenominator(elementOverride.Value)
            ? new TimberAnnotationScaleContext(
                elementOverride.Value,
                TimberAnnotationScaleSource.ElementOverride)
            : drawingContext;
    }

    public static int Resolve(
        bool hasDrawingValue,
        int drawingDenominator,
        int userDefaultDenominator)
    {
        _ = userDefaultDenominator;
        return TimberAnnotationScaleRules.NormalizeDenominator(
            hasDrawingValue
                ? drawingDenominator
                : TimberAnnotationScaleRules.DefaultDenominator);
    }

    public static TimberAnnotationScaleContext ResolveContext(
        bool hasDrawingValue,
        int drawingDenominator,
        int userDefaultDenominator)
    {
        _ = userDefaultDenominator;
        return ResolveDrawingContext(hasDrawingValue, drawingDenominator);
    }
}
