using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Drawing-level defaults and policy for automatically generated rafters.</summary>
public static class RoofRafterSpacingRules
{
    public const double DefaultAutomaticSpacingMm = 900d;
    public const double DefaultMinimumAutomaticSpacingMm = 500d;

    public static bool IsValidDimension(double spacingMm) =>
        !double.IsNaN(spacingMm) &&
        !double.IsInfinity(spacingMm) &&
        spacingMm > 0d;

    public static bool IsValidSettings(
        double defaultAutomaticSpacingMm,
        double minimumAutomaticSpacingMm,
        double minimumAutomaticLengthMm) =>
        IsValidDimension(defaultAutomaticSpacingMm) &&
        IsValidDimension(minimumAutomaticSpacingMm) &&
        defaultAutomaticSpacingMm >= minimumAutomaticSpacingMm &&
        RoofRafterLengthRules.IsValidMinimumLength(minimumAutomaticLengthMm);

    public static bool IsValidAutomaticWorkingSpacing(
        double workingSpacingMm,
        double minimumAutomaticSpacingMm) =>
        IsValidDimension(workingSpacingMm) &&
        IsValidDimension(minimumAutomaticSpacingMm) &&
        workingSpacingMm >= minimumAutomaticSpacingMm;

    public static RoofRafterSettings Resolve(
        bool hasStoredValue,
        RoofRafterSettings? storedSettings) =>
        hasStoredValue &&
        storedSettings is not null &&
        IsValidSettings(
            storedSettings.DefaultAutomaticSpacingMm,
            storedSettings.MinimumAutomaticSpacingMm,
            storedSettings.MinimumAutomaticLengthMm)
            ? storedSettings
            : RoofRafterSettings.CreateDefault();
}
