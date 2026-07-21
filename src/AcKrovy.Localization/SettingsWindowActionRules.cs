namespace AcKrovy.Localization;

public enum SettingsWindowTabKind
{
    Layers,
    Manufacturing,
    Language,
}

public enum SettingsSaveMode
{
    NewElementsOnly,
    SelectedElements,
    AllElements,
    LanguageOnly,
}

public sealed class SettingsWindowActionState
{
    internal SettingsWindowActionState(
        bool showRestoreDefaults,
        bool showApplyActions,
        bool showLanguageSave)
    {
        ShowRestoreDefaults = showRestoreDefaults;
        ShowApplyActions = showApplyActions;
        ShowLanguageSave = showLanguageSave;
    }

    public bool ShowCancel => true;
    public bool ShowRestoreDefaults { get; }
    public bool ShowApplyActions { get; }
    public bool ShowLanguageSave { get; }
}

public static class SettingsWindowActionRules
{
    public static SettingsWindowActionState ForTab(SettingsWindowTabKind tab) =>
        tab == SettingsWindowTabKind.Language
            ? new SettingsWindowActionState(false, false, true)
            : new SettingsWindowActionState(true, true, false);

    public static bool AppliesElementSettings(SettingsSaveMode saveMode) =>
        saveMode != SettingsSaveMode.LanguageOnly;
}
