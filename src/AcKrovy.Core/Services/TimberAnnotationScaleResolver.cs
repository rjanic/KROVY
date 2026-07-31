using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberAnnotationScaleResolver
{
    public static int Resolve(
        bool hasDrawingValue,
        int drawingDenominator,
        int userDefaultDenominator) =>
        TimberAnnotationScaleRules.NormalizeDenominator(
            hasDrawingValue
                ? drawingDenominator
                : userDefaultDenominator);

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
                : TimberAnnotationScaleSource.UserDefault);
}
