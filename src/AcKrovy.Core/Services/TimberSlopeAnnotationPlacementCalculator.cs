using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberSlopeAnnotationPlacementCalculator
{
    public const double SlopeAnnotationLabelClearanceMm = 100d;
    public const double MinimumEndClearanceMm = 120d;
    public const double SlopeAnnotationHalfExtentMm = 220d;

    public static TimberSlopeAnnotationPlacement Calculate(
        double elementLengthMm,
        TimberSlopeAnnotationLongitudinalInterval? labelInterval,
        double annotationHalfExtentMm = SlopeAnnotationHalfExtentMm,
        double labelClearanceMm = SlopeAnnotationLabelClearanceMm,
        double minimumEndClearanceMm = MinimumEndClearanceMm)
    {
        if (!IsFiniteNonNegative(elementLengthMm) ||
            !IsFiniteNonNegative(annotationHalfExtentMm) ||
            !IsFiniteNonNegative(labelClearanceMm) ||
            !IsFiniteNonNegative(minimumEndClearanceMm))
        {
            throw new ArgumentOutOfRangeException(nameof(elementLengthMm));
        }

        if (elementLengthMm == 0d)
        {
            return new TimberSlopeAnnotationPlacement(0d, UsesPreferredPosition: true);
        }

        var preferred = elementLengthMm * TimberSlopeArrowCalculator.SlopeAnnotationPositionFactor;
        var requestedMinimum = annotationHalfExtentMm + minimumEndClearanceMm;
        var requestedMaximum = elementLengthMm - annotationHalfExtentMm - minimumEndClearanceMm;
        var hasFullEndClearance = requestedMinimum <= requestedMaximum;
        var minimumAnchor = hasFullEndClearance ? requestedMinimum : elementLengthMm / 2d;
        var maximumAnchor = hasFullEndClearance ? requestedMaximum : elementLengthMm / 2d;
        var preferredCandidate = Clamp(preferred, minimumAnchor, maximumAnchor);

        if (labelInterval is null)
        {
            return new TimberSlopeAnnotationPlacement(
                preferredCandidate,
                UsesPreferredPosition: NearlyEqual(preferredCandidate, preferred));
        }

        var labelMinimum = Math.Min(labelInterval.MinimumMm, labelInterval.MaximumMm);
        var labelMaximum = Math.Max(labelInterval.MinimumMm, labelInterval.MaximumMm);
        if (!IsFinite(labelMinimum) || !IsFinite(labelMaximum) ||
            !Collides(preferredCandidate, annotationHalfExtentMm, labelMinimum, labelMaximum, labelClearanceMm))
        {
            return new TimberSlopeAnnotationPlacement(
                preferredCandidate,
                UsesPreferredPosition: NearlyEqual(preferredCandidate, preferred));
        }

        var startCandidate = labelMinimum - labelClearanceMm - annotationHalfExtentMm;
        if (IsWithin(startCandidate, minimumAnchor, maximumAnchor) &&
            !Collides(startCandidate, annotationHalfExtentMm, labelMinimum, labelMaximum, labelClearanceMm))
        {
            return new TimberSlopeAnnotationPlacement(startCandidate, UsesPreferredPosition: false);
        }

        var endCandidate = labelMaximum + labelClearanceMm + annotationHalfExtentMm;
        if (IsWithin(endCandidate, minimumAnchor, maximumAnchor) &&
            !Collides(endCandidate, annotationHalfExtentMm, labelMinimum, labelMaximum, labelClearanceMm))
        {
            return new TimberSlopeAnnotationPlacement(endCandidate, UsesPreferredPosition: false);
        }

        var bestCandidate = new[]
            {
                preferredCandidate,
                Clamp(startCandidate, minimumAnchor, maximumAnchor),
                Clamp(endCandidate, minimumAnchor, maximumAnchor),
                minimumAnchor,
                maximumAnchor,
            }
            .Distinct()
            .OrderBy(candidate => OverlapMm(
                candidate - annotationHalfExtentMm,
                candidate + annotationHalfExtentMm,
                labelMinimum - labelClearanceMm,
                labelMaximum + labelClearanceMm))
            .ThenBy(candidate => Math.Abs(candidate - preferred))
            .ThenBy(candidate => candidate)
            .First();

        return new TimberSlopeAnnotationPlacement(
            bestCandidate,
            UsesPreferredPosition: NearlyEqual(bestCandidate, preferred));
    }

    private static bool Collides(
        double anchor,
        double annotationHalfExtent,
        double labelMinimum,
        double labelMaximum,
        double clearance) =>
        anchor + annotationHalfExtent + clearance > labelMinimum &&
        anchor - annotationHalfExtent - clearance < labelMaximum;

    private static double OverlapMm(double firstMinimum, double firstMaximum, double secondMinimum, double secondMaximum) =>
        Math.Max(0d, Math.Min(firstMaximum, secondMaximum) - Math.Max(firstMinimum, secondMinimum));

    private static bool IsWithin(double value, double minimum, double maximum) =>
        value >= minimum && value <= maximum;

    private static double Clamp(double value, double minimum, double maximum) =>
        Math.Max(minimum, Math.Min(maximum, value));

    private static bool NearlyEqual(double first, double second) => Math.Abs(first - second) < 0.001d;

    private static bool IsFiniteNonNegative(double value) => IsFinite(value) && value >= 0d;

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
