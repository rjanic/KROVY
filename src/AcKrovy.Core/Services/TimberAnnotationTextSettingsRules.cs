using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberAnnotationTextSettingsRules
{
    public const string DefaultTextStyleName = "Standard";
    public const int MaximumTextStyleNameLength = 255;

    public const double DefaultLabelAndDimensionPaperHeightMm = 2.5d;
    public const double MinimumLabelAndDimensionPaperHeightMm = 1d;
    public const double MaximumLabelAndDimensionPaperHeightMm = 10d;

    public const double DefaultItemNumberPaperHeightMm = 2.7d;
    public const double MinimumItemNumberPaperHeightMm = 1d;
    public const double MaximumItemNumberPaperHeightMm = 3.5d;

    public const double DefaultSlopeAnglePaperHeightMm = 1.6d;
    public const double MinimumSlopeAnglePaperHeightMm = 1d;
    public const double MaximumSlopeAnglePaperHeightMm = 5d;

    public static TimberAnnotationTextSettings Default { get; } = new(
        DefaultTextStyleName,
        DefaultLabelAndDimensionPaperHeightMm,
        DefaultItemNumberPaperHeightMm,
        DefaultSlopeAnglePaperHeightMm);

    public static bool IsValidTextStyleName(string? textStyleName)
    {
        var normalized = textStyleName?.Trim();
        return normalized is not null &&
            normalized.Length > 0 &&
            normalized.Length <= MaximumTextStyleNameLength &&
            !normalized.Any(char.IsControl);
    }

    public static bool IsValidLabelAndDimensionPaperHeightMm(double value) =>
        IsWithinInclusiveRange(
            value,
            MinimumLabelAndDimensionPaperHeightMm,
            MaximumLabelAndDimensionPaperHeightMm);

    public static bool IsValidItemNumberPaperHeightMm(double value) =>
        IsWithinInclusiveRange(
            value,
            MinimumItemNumberPaperHeightMm,
            MaximumItemNumberPaperHeightMm);

    public static bool IsValidSlopeAnglePaperHeightMm(double value) =>
        IsWithinInclusiveRange(
            value,
            MinimumSlopeAnglePaperHeightMm,
            MaximumSlopeAnglePaperHeightMm);

    public static bool IsValid(TimberAnnotationTextSettings? settings) =>
        settings is not null &&
        IsValidTextStyleName(settings.TextStyleName) &&
        IsValidLabelAndDimensionPaperHeightMm(
            settings.LabelAndDimensionPaperHeightMm) &&
        IsValidItemNumberPaperHeightMm(settings.ItemNumberPaperHeightMm) &&
        IsValidSlopeAnglePaperHeightMm(settings.SlopeAnglePaperHeightMm);

    public static TimberAnnotationTextSettings ValidateAndNormalize(
        TimberAnnotationTextSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (!IsValidTextStyleName(settings.TextStyleName))
        {
            throw new ArgumentException(
                $"Text style name must contain between 1 and {MaximumTextStyleNameLength} non-control characters.",
                nameof(settings));
        }

        ValidateHeight(
            settings.LabelAndDimensionPaperHeightMm,
            MinimumLabelAndDimensionPaperHeightMm,
            MaximumLabelAndDimensionPaperHeightMm,
            nameof(settings.LabelAndDimensionPaperHeightMm));
        ValidateHeight(
            settings.ItemNumberPaperHeightMm,
            MinimumItemNumberPaperHeightMm,
            MaximumItemNumberPaperHeightMm,
            nameof(settings.ItemNumberPaperHeightMm));
        ValidateHeight(
            settings.SlopeAnglePaperHeightMm,
            MinimumSlopeAnglePaperHeightMm,
            MaximumSlopeAnglePaperHeightMm,
            nameof(settings.SlopeAnglePaperHeightMm));

        return settings with { TextStyleName = settings.TextStyleName.Trim() };
    }

    /// <summary>
    /// Preserves a missing legacy value. Invalid persisted fields fall back to
    /// their factory defaults rather than being clamped to a range boundary.
    /// </summary>
    public static TimberAnnotationTextSettings? NormalizeStored(
        TimberAnnotationTextSettings? settings)
    {
        if (settings is null)
        {
            return null;
        }

        return new TimberAnnotationTextSettings(
            IsValidTextStyleName(settings.TextStyleName)
                ? settings.TextStyleName.Trim()
                : DefaultTextStyleName,
            IsValidLabelAndDimensionPaperHeightMm(
                settings.LabelAndDimensionPaperHeightMm)
                ? settings.LabelAndDimensionPaperHeightMm
                : DefaultLabelAndDimensionPaperHeightMm,
            IsValidItemNumberPaperHeightMm(settings.ItemNumberPaperHeightMm)
                ? settings.ItemNumberPaperHeightMm
                : DefaultItemNumberPaperHeightMm,
            IsValidSlopeAnglePaperHeightMm(settings.SlopeAnglePaperHeightMm)
                ? settings.SlopeAnglePaperHeightMm
                : DefaultSlopeAnglePaperHeightMm);
    }

    public static double CalculateModelHeightMm(
        double paperHeightMm,
        int annotationScaleDenominator)
    {
        if (paperHeightMm <= 0d ||
            double.IsNaN(paperHeightMm) ||
            double.IsInfinity(paperHeightMm))
        {
            throw new ArgumentOutOfRangeException(nameof(paperHeightMm));
        }
        if (!TimberAnnotationScaleRules.IsValidDenominator(
                annotationScaleDenominator))
        {
            throw new ArgumentOutOfRangeException(
                nameof(annotationScaleDenominator),
                annotationScaleDenominator,
                $"Annotation scale denominator must be between {TimberAnnotationScaleRules.MinimumDenominator} and {TimberAnnotationScaleRules.MaximumDenominator}.");
        }

        return paperHeightMm * annotationScaleDenominator;
    }

    private static bool IsWithinInclusiveRange(
        double value,
        double minimum,
        double maximum) =>
        !double.IsNaN(value) &&
        !double.IsInfinity(value) &&
        value >= minimum &&
        value <= maximum;

    private static void ValidateHeight(
        double value,
        double minimum,
        double maximum,
        string parameterName)
    {
        if (!IsWithinInclusiveRange(value, minimum, maximum))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Paper text height must be between {minimum} and {maximum} millimetres.");
        }
    }
}
