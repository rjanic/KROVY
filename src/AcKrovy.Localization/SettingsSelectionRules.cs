using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.Localization;

public static class SettingsSelectionRules
{
    public static TimberAnnotationMode NormalizeAnnotationMode(TimberAnnotationMode value) =>
        TimberAnnotationModeRules.Normalize(value);

    public static ItemNumberLeaderStyle NormalizeItemNumberLeaderStyle(
        ItemNumberLeaderStyle value) =>
        ItemNumberLeaderStyleRules.Normalize(value);

    public static SettingsSaveMode NormalizeApplyMode(SettingsSaveMode value) =>
        value is SettingsSaveMode.NewElementsOnly
            or SettingsSaveMode.SelectedElements
            or SettingsSaveMode.AllElements
                ? value
                : SettingsSaveMode.NewElementsOnly;
}
