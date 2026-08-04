using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.Localization;

/// <summary>
/// Formats paper-height range hints and validation messages from
/// <see cref="TimberAnnotationTextSettingsRules"/> Min/Max constants.
/// </summary>
public static class AnnotationTextHeightValidationMessages
{
    public static string FormatAllowedRange(
        TimberAnnotationTextRole role,
        CultureInfo? culture = null)
    {
        var uiCulture = culture ?? AppLanguageService.CurrentUiCulture;
        return UiStrings.Format(
            UiStrings.GetString(
                "SettingsWindow_AnnotationText_AllowedRangeFormat",
                uiCulture),
            FormatHeight(GetMinimum(role), uiCulture),
            FormatHeight(GetMaximum(role), uiCulture));
    }

    public static string FormatInlineError(
        TimberAnnotationTextRole role,
        CultureInfo? culture = null)
    {
        var uiCulture = culture ?? AppLanguageService.CurrentUiCulture;
        return UiStrings.Format(
            UiStrings.GetString(
                "SettingsWindow_AnnotationText_HeightInlineErrorFormat",
                uiCulture),
            FormatHeight(GetMinimum(role), uiCulture),
            FormatHeight(GetMaximum(role), uiCulture));
    }

    public static string FormatSaveError(
        TimberAnnotationTextRole role,
        CultureInfo? culture = null)
    {
        var uiCulture = culture ?? AppLanguageService.CurrentUiCulture;
        return UiStrings.Format(
            UiStrings.GetString(GetSaveErrorResourceKey(role), uiCulture),
            FormatHeight(GetMinimum(role), uiCulture),
            FormatHeight(GetMaximum(role), uiCulture));
    }

    public static string FormatNumericRequired(CultureInfo? culture = null) =>
        UiStrings.GetString(
            "SettingsWindow_AnnotationText_NumericValueRequired",
            culture ?? AppLanguageService.CurrentUiCulture);

    public static string FormatHeight(double value, CultureInfo culture) =>
        value.ToString("0.0", culture);

    public static double GetMinimum(TimberAnnotationTextRole role) =>
        TimberAnnotationTextSettingsRules.GetMinimumPaperHeightMm(role);

    public static double GetMaximum(TimberAnnotationTextRole role) =>
        TimberAnnotationTextSettingsRules.GetMaximumPaperHeightMm(role);

    public static string GetSaveErrorResourceKey(TimberAnnotationTextRole role) =>
        role switch
        {
            TimberAnnotationTextRole.ItemCode =>
                "SettingsWindow_AnnotationText_HeightRangeError_ItemCode",
            TimberAnnotationTextRole.Dimension =>
                "SettingsWindow_AnnotationText_HeightRangeError_Dimension",
            TimberAnnotationTextRole.Slope =>
                "SettingsWindow_AnnotationText_HeightRangeError_Slope",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
}
