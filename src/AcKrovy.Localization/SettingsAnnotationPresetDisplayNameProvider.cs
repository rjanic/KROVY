using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.Localization;

public static class SettingsAnnotationPresetDisplayNameProvider
{
    public static string GetDisplayName(
        SettingsAnnotationPreset preset,
        CultureInfo? culture = null) =>
        UiStrings.GetString(
            SettingsAnnotationPresetRules.Get(preset).LocalizationKey,
            culture);
}
