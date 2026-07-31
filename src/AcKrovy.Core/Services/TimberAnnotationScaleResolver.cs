using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberAnnotationScaleResolver
{
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
        int userDefaultDenominator) =>
        new(
            Resolve(
                hasDrawingValue,
                drawingDenominator,
                userDefaultDenominator),
            hasDrawingValue
                ? TimberAnnotationScaleSource.Drawing
                : TimberAnnotationScaleSource.FixedDefault);
}
