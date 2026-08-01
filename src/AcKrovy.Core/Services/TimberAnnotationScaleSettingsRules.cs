namespace AcKrovy.Core.Services;

public enum TimberAnnotationScalePreset
{
    Scale25,
    Scale50,
    Scale75,
    Scale100,
    Custom,
}

public sealed record TimberAnnotationScalePreview(
    double DimensionTextHeightMm,
    double DimensionPaperTextHeightMm,
    double ItemNumberTextHeightMm,
    double ItemNumberPaperTextHeightMm,
    double SlopeTextHeightMm,
    double SlopePaperTextHeightMm,
    double FramedBlockScale);

public static class TimberAnnotationScaleSettingsRules
{
    public static TimberAnnotationScalePreset GetPreset(int denominator) =>
        TimberAnnotationScaleRules.NormalizeDenominator(denominator) switch
        {
            25 => TimberAnnotationScalePreset.Scale25,
            50 => TimberAnnotationScalePreset.Scale50,
            75 => TimberAnnotationScalePreset.Scale75,
            100 => TimberAnnotationScalePreset.Scale100,
            _ => TimberAnnotationScalePreset.Custom,
        };

    public static int GetPresetDenominator(
        TimberAnnotationScalePreset preset,
        int customDenominator) =>
        preset switch
        {
            TimberAnnotationScalePreset.Scale25 => 25,
            TimberAnnotationScalePreset.Scale50 => 50,
            TimberAnnotationScalePreset.Scale75 => 75,
            TimberAnnotationScalePreset.Scale100 => 100,
            TimberAnnotationScalePreset.Custom
                when TimberAnnotationScaleRules.IsValidDenominator(customDenominator) =>
                customDenominator,
            _ => TimberAnnotationScaleRules.DefaultDenominator,
        };

    public static TimberAnnotationScalePreview CreatePreview(int denominator)
    {
        var normalizedDenominator =
            TimberAnnotationScaleRules.NormalizeDenominator(denominator);
        var scaleFactor = TimberAnnotationScaleRules.GetScaleFactor(
            normalizedDenominator);
        var dimensionModel =
            TimberDimensionTypographyRules.CalculateTextHeightMm(scaleFactor);
        var itemNumberModel =
            TimberItemNumberTypographyRules.CalculateTextHeightMm(scaleFactor);
        var slopeModel =
            TimberSlopeAnnotationPresentationRules.CalculateTextHeightMm(scaleFactor);
        return new TimberAnnotationScalePreview(
            dimensionModel,
            dimensionModel / normalizedDenominator,
            itemNumberModel,
            itemNumberModel / normalizedDenominator,
            slopeModel,
            slopeModel / normalizedDenominator,
            scaleFactor);
    }
}
