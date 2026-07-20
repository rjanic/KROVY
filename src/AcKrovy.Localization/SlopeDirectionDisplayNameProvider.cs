namespace AcKrovy.Localization;

public static class SlopeDirectionDisplayNameProvider
{
    public static string GetDisplayName(bool isReversed, System.Globalization.CultureInfo? culture = null) =>
        isReversed
            ? UiStrings.GetString("EditWindow_SlopeDirectionReversed", culture)
            : UiStrings.GetString("EditWindow_SlopeDirectionNormal", culture);
}
