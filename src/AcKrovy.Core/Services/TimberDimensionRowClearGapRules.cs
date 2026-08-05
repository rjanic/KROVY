namespace AcKrovy.Core.Services;

/// <summary>
/// WIDTH/HEIGHT clear-gap contract for G5 BlockContent annotations.
/// DesiredClearGapPaperMm is the clear space between glyph envelopes, not
/// the center-to-center distance.
/// </summary>
public static class TimberDimensionRowClearGapRules
{
    /// <summary>
    /// Clear paper-space gap between the bottom of WIDTH and the top of HEIGHT
    /// (MidCenter AttrDefs of equal height).
    /// </summary>
    public const double DesiredClearGapPaperMm = 2.0d;

    public const double LandingLocalY = 0d;

    public static double CalculateDimensionTextModelHeightMm(
        double dimensionTextPaperHeightMm,
        int annotationScaleDenominator) =>
        TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
            dimensionTextPaperHeightMm,
            annotationScaleDenominator);

    public static double CalculateDesiredClearGapModelMm(
        int annotationScaleDenominator)
    {
        if (!TimberAnnotationScaleRules.IsValidDenominator(annotationScaleDenominator))
        {
            throw new ArgumentOutOfRangeException(
                nameof(annotationScaleDenominator),
                annotationScaleDenominator,
                $"Annotation scale denominator must be between {TimberAnnotationScaleRules.MinimumDenominator} and {TimberAnnotationScaleRules.MaximumDenominator}.");
        }

        return DesiredClearGapPaperMm * annotationScaleDenominator;
    }

    public static double CalculateRowCenterDistanceModelMm(
        double dimensionTextPaperHeightMm,
        int annotationScaleDenominator)
    {
        var textHeight = CalculateDimensionTextModelHeightMm(
            dimensionTextPaperHeightMm,
            annotationScaleDenominator);
        var clearGap = CalculateDesiredClearGapModelMm(annotationScaleDenominator);
        return textHeight + clearGap;
    }

    public static double CalculateHalfRowCenterDistanceModelMm(
        double dimensionTextPaperHeightMm,
        int annotationScaleDenominator) =>
        CalculateRowCenterDistanceModelMm(
            dimensionTextPaperHeightMm,
            annotationScaleDenominator) / 2d;

    public static double CalculateWidthLocalY(
        double dimensionTextPaperHeightMm,
        int annotationScaleDenominator,
        double landingLocalY = LandingLocalY) =>
        landingLocalY +
        CalculateHalfRowCenterDistanceModelMm(
            dimensionTextPaperHeightMm,
            annotationScaleDenominator);

    public static double CalculateHeightLocalY(
        double dimensionTextPaperHeightMm,
        int annotationScaleDenominator,
        double landingLocalY = LandingLocalY) =>
        landingLocalY -
        CalculateHalfRowCenterDistanceModelMm(
            dimensionTextPaperHeightMm,
            annotationScaleDenominator);

    /// <summary>
    /// Clear glyph gap implied by an observed center-to-center distance
    /// for equal-height MidCenter rows: centerDistance − textHeight.
    /// </summary>
    public static double CalculateActualGlyphClearGapModelMm(
        double actualCenterDistanceModelMm,
        double dimensionTextModelHeightMm)
    {
        if (actualCenterDistanceModelMm < 0d ||
            double.IsNaN(actualCenterDistanceModelMm) ||
            double.IsInfinity(actualCenterDistanceModelMm))
        {
            throw new ArgumentOutOfRangeException(nameof(actualCenterDistanceModelMm));
        }
        if (dimensionTextModelHeightMm <= 0d ||
            double.IsNaN(dimensionTextModelHeightMm) ||
            double.IsInfinity(dimensionTextModelHeightMm))
        {
            throw new ArgumentOutOfRangeException(nameof(dimensionTextModelHeightMm));
        }

        return actualCenterDistanceModelMm - dimensionTextModelHeightMm;
    }
}
