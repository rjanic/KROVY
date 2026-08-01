namespace AcKrovy.Core.Services;

public static class TimberSlopeAnnotationPresentationRules
{
    public const double BaseTextHeightAtScale50Mm =
        TimberAnnotationTextSettingsRules.DefaultSlopeAnglePaperHeightMm *
        TimberAnnotationScaleRules.DefaultDenominator;
    public const double BaseTextOffsetAtScale50Mm = 100d;
    public const double LegacySymbolTextHeightMm = 120d;
    public const double SpecialSymbolBaseReductionFactor =
        BaseTextHeightAtScale50Mm / LegacySymbolTextHeightMm;
    public const int DefaultLayerColorIndex = 40;

    public static double CalculateTextHeightMm(
        double presentationScaleFactor) =>
        BaseTextHeightAtScale50Mm *
        ValidatePresentationScaleFactor(presentationScaleFactor);

    public static double CalculateTextOffsetMm(
        double presentationScaleFactor) =>
        BaseTextOffsetAtScale50Mm *
        ValidatePresentationScaleFactor(presentationScaleFactor);

    public static double CalculateSpecialSymbolScale(
        double presentationScaleFactor) =>
        SpecialSymbolBaseReductionFactor *
        ValidatePresentationScaleFactor(presentationScaleFactor);

    public static double ScaleLength(
        double baseLengthMm,
        double presentationScaleFactor)
    {
        if (baseLengthMm < 0d ||
            double.IsNaN(baseLengthMm) ||
            double.IsInfinity(baseLengthMm))
        {
            throw new ArgumentOutOfRangeException(nameof(baseLengthMm));
        }

        return baseLengthMm *
            ValidatePresentationScaleFactor(presentationScaleFactor);
    }

    private static double ValidatePresentationScaleFactor(
        double presentationScaleFactor)
    {
        if (presentationScaleFactor <= 0d ||
            double.IsNaN(presentationScaleFactor) ||
            double.IsInfinity(presentationScaleFactor))
        {
            throw new ArgumentOutOfRangeException(
                nameof(presentationScaleFactor));
        }

        return presentationScaleFactor;
    }
}
