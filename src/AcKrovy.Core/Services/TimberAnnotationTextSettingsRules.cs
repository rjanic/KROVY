using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberAnnotationTextSettingsRules
{
    public const string DefaultTextStyleName = "Standard";
    public const int MaximumTextStyleNameLength = 255;

    public const double DefaultItemCodePaperHeightMm = 2.7d;
    public const double MinimumItemCodePaperHeightMm = 1d;
    public const double MaximumItemCodePaperHeightMm = 3.5d;

    public const double DefaultDimensionPaperHeightMm = 2.5d;
    public const double MinimumDimensionPaperHeightMm = 1d;
    public const double MaximumDimensionPaperHeightMm = 10d;

    public const double DefaultSlopePaperHeightMm = 1.6d;
    public const double MinimumSlopePaperHeightMm = 1d;
    public const double MaximumSlopePaperHeightMm = 5d;

    public static TimberAnnotationTextSettings Default { get; } =
        TimberAnnotationTextSettings.Shared(
            DefaultTextStyleName,
            DefaultItemCodePaperHeightMm,
            DefaultDimensionPaperHeightMm,
            DefaultSlopePaperHeightMm);

    public static double GetDefaultPaperHeightMm(TimberAnnotationTextRole role) =>
        role switch
        {
            TimberAnnotationTextRole.ItemCode => DefaultItemCodePaperHeightMm,
            TimberAnnotationTextRole.Dimension => DefaultDimensionPaperHeightMm,
            TimberAnnotationTextRole.Slope => DefaultSlopePaperHeightMm,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };

    public static double GetMinimumPaperHeightMm(TimberAnnotationTextRole role) =>
        role switch
        {
            TimberAnnotationTextRole.ItemCode => MinimumItemCodePaperHeightMm,
            TimberAnnotationTextRole.Dimension => MinimumDimensionPaperHeightMm,
            TimberAnnotationTextRole.Slope => MinimumSlopePaperHeightMm,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };

    public static double GetMaximumPaperHeightMm(TimberAnnotationTextRole role) =>
        role switch
        {
            TimberAnnotationTextRole.ItemCode => MaximumItemCodePaperHeightMm,
            TimberAnnotationTextRole.Dimension => MaximumDimensionPaperHeightMm,
            TimberAnnotationTextRole.Slope => MaximumSlopePaperHeightMm,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };

    public static bool IsValidTextStyleName(string? textStyleName)
    {
        var normalized = textStyleName?.Trim();
        return normalized is not null &&
            normalized.Length > 0 &&
            normalized.Length <= MaximumTextStyleNameLength &&
            !normalized.Any(char.IsControl);
    }

    public static bool IsValidPaperHeightMm(
        TimberAnnotationTextRole role,
        double value) =>
        IsWithinInclusiveRange(
            value,
            GetMinimumPaperHeightMm(role),
            GetMaximumPaperHeightMm(role));

    public static bool IsValidItemCodePaperHeightMm(double value) =>
        IsValidPaperHeightMm(TimberAnnotationTextRole.ItemCode, value);

    public static bool IsValidDimensionPaperHeightMm(double value) =>
        IsValidPaperHeightMm(TimberAnnotationTextRole.Dimension, value);

    public static bool IsValidSlopePaperHeightMm(double value) =>
        IsValidPaperHeightMm(TimberAnnotationTextRole.Slope, value);

    public static bool IsValid(TimberAnnotationTextSettings? settings) =>
        settings is not null &&
        IsValidTextStyleName(settings.ItemCodeTextStyleName) &&
        IsValidTextStyleName(settings.DimensionTextStyleName) &&
        IsValidTextStyleName(settings.SlopeTextStyleName) &&
        IsValidItemCodePaperHeightMm(settings.ItemCodePaperHeightMm) &&
        IsValidDimensionPaperHeightMm(settings.DimensionPaperHeightMm) &&
        IsValidSlopePaperHeightMm(settings.SlopePaperHeightMm);

    public static TimberAnnotationTextSettings ValidateAndNormalize(
        TimberAnnotationTextSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        ValidateTextStyleName(
            settings.ItemCodeTextStyleName,
            nameof(settings.ItemCodeTextStyleName));
        ValidateTextStyleName(
            settings.DimensionTextStyleName,
            nameof(settings.DimensionTextStyleName));
        ValidateTextStyleName(
            settings.SlopeTextStyleName,
            nameof(settings.SlopeTextStyleName));

        ValidateHeight(
            TimberAnnotationTextRole.ItemCode,
            settings.ItemCodePaperHeightMm,
            nameof(settings.ItemCodePaperHeightMm));
        ValidateHeight(
            TimberAnnotationTextRole.Dimension,
            settings.DimensionPaperHeightMm,
            nameof(settings.DimensionPaperHeightMm));
        ValidateHeight(
            TimberAnnotationTextRole.Slope,
            settings.SlopePaperHeightMm,
            nameof(settings.SlopePaperHeightMm));

        return settings with
        {
            ItemCodeTextStyleName = settings.ItemCodeTextStyleName.Trim(),
            DimensionTextStyleName = settings.DimensionTextStyleName.Trim(),
            SlopeTextStyleName = settings.SlopeTextStyleName.Trim(),
        };
    }

    /// <summary>
    /// Validates one role in isolation so a role-scoped patch never depends on
    /// the other two roles.
    /// </summary>
    public static string ValidateAndNormalizeTextStyleName(
        string textStyleName,
        string parameterName)
    {
        ValidateTextStyleName(textStyleName, parameterName);
        return textStyleName.Trim();
    }

    public static double ValidatePaperHeightMm(
        TimberAnnotationTextRole role,
        double paperHeightMm,
        string parameterName)
    {
        ValidateHeight(role, paperHeightMm, parameterName);
        return paperHeightMm;
    }

    /// <summary>
    /// Preserves a missing legacy value. Invalid persisted fields fall back to
    /// their factory defaults rather than being clamped to a range boundary.
    /// A schema 6 payload carries one shared style name, so the dimension and
    /// slope roles fall back to the item-code style before the factory style.
    /// </summary>
    public static TimberAnnotationTextSettings? NormalizeStored(
        TimberAnnotationTextSettings? settings)
    {
        if (settings is null)
        {
            return null;
        }

        var itemCodeTextStyleName = IsValidTextStyleName(settings.ItemCodeTextStyleName)
            ? NormalizeLegacyBuiltInStyleName(settings.ItemCodeTextStyleName)
            : DefaultTextStyleName;

        return new TimberAnnotationTextSettings(
            itemCodeTextStyleName,
            IsValidTextStyleName(settings.DimensionTextStyleName)
                ? NormalizeLegacyBuiltInStyleName(settings.DimensionTextStyleName)
                : itemCodeTextStyleName,
            IsValidTextStyleName(settings.SlopeTextStyleName)
                ? NormalizeLegacyBuiltInStyleName(settings.SlopeTextStyleName)
                : itemCodeTextStyleName,
            IsValidItemCodePaperHeightMm(settings.ItemCodePaperHeightMm)
                ? settings.ItemCodePaperHeightMm
                : DefaultItemCodePaperHeightMm,
            IsValidDimensionPaperHeightMm(settings.DimensionPaperHeightMm)
                ? settings.DimensionPaperHeightMm
                : DefaultDimensionPaperHeightMm,
            IsValidSlopePaperHeightMm(settings.SlopePaperHeightMm)
                ? settings.SlopePaperHeightMm
                : DefaultSlopePaperHeightMm);
    }

    private static string NormalizeLegacyBuiltInStyleName(string styleName)
    {
        var normalized = styleName.Trim();
        return string.Equals(
                normalized,
                TimberAnnotationTextStylePresetRules.LegacyArchitecturalStyleName,
                StringComparison.OrdinalIgnoreCase)
            ? TimberAnnotationTextStylePresetRules.ArchitecturalStyleName
            : normalized;
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

    private static void ValidateTextStyleName(
        string? textStyleName,
        string parameterName)
    {
        if (!IsValidTextStyleName(textStyleName))
        {
            throw new ArgumentException(
                $"Text style name must contain between 1 and {MaximumTextStyleNameLength} non-control characters.",
                parameterName);
        }
    }

    private static void ValidateHeight(
        TimberAnnotationTextRole role,
        double value,
        string parameterName)
    {
        var minimum = GetMinimumPaperHeightMm(role);
        var maximum = GetMaximumPaperHeightMm(role);
        if (!IsWithinInclusiveRange(value, minimum, maximum))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Paper text height must be between {minimum} and {maximum} millimetres.");
        }
    }
}
