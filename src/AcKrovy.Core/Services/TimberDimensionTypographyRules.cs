namespace AcKrovy.Core.Services;

public static class TimberDimensionTypographyRules
{
    public const double BaseDimensionTextHeightAtScale50Mm =
        TimberAnnotationTextSettingsRules
            .DefaultLabelAndDimensionPaperHeightMm *
        TimberAnnotationScaleRules.DefaultDenominator;
    public const double MTextLineAdvanceFactor = 5d / 3d;

    public static double CalculateTextHeightMm(double presentationScaleFactor)
    {
        if (presentationScaleFactor <= 0d ||
            double.IsNaN(presentationScaleFactor) ||
            double.IsInfinity(presentationScaleFactor))
        {
            throw new ArgumentOutOfRangeException(
                nameof(presentationScaleFactor));
        }

        return BaseDimensionTextHeightAtScale50Mm * presentationScaleFactor;
    }

    public static double CalculateLineAdvanceMm(double textHeightMm)
    {
        ValidateTextHeight(textHeightMm);
        return textHeightMm * MTextLineAdvanceFactor;
    }

    public static double CalculateFullLabelCenterOffsetMm(
        double textHeightMm) =>
        CalculateLineAdvanceMm(textHeightMm) / 2d;

    private static void ValidateTextHeight(double textHeightMm)
    {
        if (textHeightMm <= 0d ||
            double.IsNaN(textHeightMm) ||
            double.IsInfinity(textHeightMm))
        {
            throw new ArgumentOutOfRangeException(nameof(textHeightMm));
        }
    }
}
