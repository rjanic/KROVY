using System.Globalization;
using AcKrovy.Core.Models;

namespace AcKrovy.Localization;

public static class SettingsTextStylePresetDisplayNameProvider
{
    public static string GetDisplayName(
        TimberAnnotationTextStylePresetDefinition definition,
        CultureInfo? culture = null)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        if (definition.Kind == TimberAnnotationTextStylePresetKind.BuiltIn &&
            !string.IsNullOrWhiteSpace(definition.LocalizationKey))
        {
            return UiStrings.GetString(definition.LocalizationKey!, culture);
        }

        if (!string.IsNullOrWhiteSpace(definition.DisplayName))
        {
            return definition.DisplayName!.Trim();
        }

        return definition.AutoCadTextStyleName;
    }
}
