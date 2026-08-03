namespace AcKrovy.Core.Services;

public static class TimberItemNumberTypographyRules
{
    public const double BaseItemNumberTextHeightAtScale50Mm =
        TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm *
        TimberAnnotationScaleRules.DefaultDenominator;
    public const double PlainItemNumberTextCenterOffsetAtScale50Mm =
        TimberLeaderPlacementCalculator.DefaultTextOffsetMm;
    public const double PlainItemNumberTextClearanceAtScale50Mm =
        PlainItemNumberTextCenterOffsetAtScale50Mm -
        BaseItemNumberTextHeightAtScale50Mm / 2d;

    public static double CalculateTextHeightMm(
        double presentationScaleFactor) =>
        BaseItemNumberTextHeightAtScale50Mm *
        ValidateScaleFactor(presentationScaleFactor);

    public static double CalculatePlainTextClearanceMm(
        double presentationScaleFactor) =>
        PlainItemNumberTextClearanceAtScale50Mm *
        ValidateScaleFactor(presentationScaleFactor);

    private static double ValidateScaleFactor(double presentationScaleFactor)
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
