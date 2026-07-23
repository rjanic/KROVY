using System.Globalization;

namespace AcKrovy.Localization;

public static class RenumberConfirmationRules
{
    public const string SlovakAsciiYesKeyword = "Ano";
    public const string SlovakAsciiYesShortcut = "A";
    public const string SlovakYesShortcut = "Á";

    public static bool SupportsSlovakAsciiYesAlias(CultureInfo culture)
    {
        if (culture is null)
        {
            throw new ArgumentNullException(nameof(culture));
        }

        return string.Equals(culture.TwoLetterISOLanguageName, "sk", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsConfirmed(
        string? keywordResult,
        string? localizedYes,
        CultureInfo? culture = null)
    {
        if (string.IsNullOrWhiteSpace(keywordResult))
        {
            return false;
        }

        var effectiveCulture = culture ?? CultureInfo.CurrentUICulture;
        var normalizedResult = keywordResult!.Trim();
        if (string.Equals(normalizedResult, "Yes", StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(localizedYes) &&
             effectiveCulture.CompareInfo.Compare(
                 normalizedResult,
                 localizedYes!.Trim(),
                 CompareOptions.IgnoreCase) == 0))
        {
            return true;
        }

        return SupportsSlovakAsciiYesAlias(effectiveCulture) &&
               (string.Equals(
                    normalizedResult,
                    SlovakAsciiYesKeyword,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    normalizedResult,
                    SlovakAsciiYesShortcut,
                    StringComparison.OrdinalIgnoreCase) ||
                effectiveCulture.CompareInfo.Compare(
                    normalizedResult,
                    SlovakYesShortcut,
                    CompareOptions.IgnoreCase) == 0);
    }
}
