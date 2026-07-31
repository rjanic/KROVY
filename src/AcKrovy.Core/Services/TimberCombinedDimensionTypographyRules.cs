namespace AcKrovy.Core.Services;

public static class TimberCombinedDimensionTypographyRules
{
    public const double BaseDimensionTextHeightAtScale50Mm =
        TimberDimensionTypographyRules.BaseDimensionTextHeightAtScale50Mm;
    public const double BaseMinimumFrameGapAtScale50Mm = 75d;
    public const double EstimatedCharacterWidthFactor = 0.62d;

    public static double CalculateTextHeightMm(double presentationScaleFactor) =>
        TimberDimensionTypographyRules.CalculateTextHeightMm(
            presentationScaleFactor);

    public static double CalculateEnvelopeHeightMm(double presentationScaleFactor) =>
        CalculateTextHeightMm(presentationScaleFactor);

    public static double CalculateEnvelopeWidthMm(
        string? contents,
        double presentationScaleFactor)
    {
        var textHeightMm = CalculateTextHeightMm(presentationScaleFactor);
        var maximumLineLength = (contents ?? string.Empty)
            .Replace("\r\n", "\n")
            .Split(["\\P", "\n"], StringSplitOptions.None)
            .Max(line => line.Length);
        return Math.Max(
            textHeightMm,
            maximumLineLength *
            textHeightMm *
            EstimatedCharacterWidthFactor);
    }

    public static double CalculateMinimumFrameGapMm(double presentationScaleFactor) =>
        BaseMinimumFrameGapAtScale50Mm *
        ValidatePresentationScaleFactor(presentationScaleFactor);

    public static double CalculateTextCenterOffsetFromLandingStartMm(
        double landingDistanceMm,
        double dimensionTextEnvelopeWidthMm,
        double dimensionTextHeightMm)
    {
        if (landingDistanceMm <= 0d ||
            double.IsNaN(landingDistanceMm) ||
            double.IsInfinity(landingDistanceMm))
        {
            throw new ArgumentOutOfRangeException(nameof(landingDistanceMm));
        }
        if (dimensionTextHeightMm <= 0d ||
            double.IsNaN(dimensionTextHeightMm) ||
            double.IsInfinity(dimensionTextHeightMm))
        {
            throw new ArgumentOutOfRangeException(nameof(dimensionTextHeightMm));
        }
        if (dimensionTextEnvelopeWidthMm <= 0d ||
            double.IsNaN(dimensionTextEnvelopeWidthMm) ||
            double.IsInfinity(dimensionTextEnvelopeWidthMm))
        {
            throw new ArgumentOutOfRangeException(
                nameof(dimensionTextEnvelopeWidthMm));
        }

        var presentationScaleFactor =
            dimensionTextHeightMm / BaseDimensionTextHeightAtScale50Mm;
        var minimumFrameGapMm =
            CalculateMinimumFrameGapMm(presentationScaleFactor);
        return Math.Max(
            0d,
            landingDistanceMm -
            minimumFrameGapMm -
            dimensionTextEnvelopeWidthMm / 2d);
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
