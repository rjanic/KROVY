namespace AcKrovy.Localization;

public enum SettingsTheme
{
    Light,
    Dark,
}

public enum SettingsFormState
{
    NoChanges,
    UnsavedChanges,
    Applied,
}

public static class SettingsFashionLookRules
{
    public const double DefaultWindowWidth = 1500d;
    public const double DefaultWindowHeight = 900d;
    public const double MinimumWindowWidth = 1250d;
    public const double MinimumWindowHeight = 720d;

    public static IReadOnlyList<SettingsWindowTabKind> NavigationSections { get; } =
    [
        SettingsWindowTabKind.Layers,
        SettingsWindowTabKind.Manufacturing,
        SettingsWindowTabKind.Annotation,
        SettingsWindowTabKind.Language,
    ];

    public static SettingsWindowTabKind NormalizeSection(SettingsWindowTabKind value) =>
        NavigationSections.Contains(value) ? value : SettingsWindowTabKind.Layers;

    public static SettingsTheme NormalizeTheme(SettingsTheme value) =>
        value is SettingsTheme.Light or SettingsTheme.Dark ? value : SettingsTheme.Light;

    public static bool UiOnlyChangeDispatchesApply() => false;

    public static string FormStateResourceKey(SettingsFormState state) =>
        state switch
        {
            SettingsFormState.UnsavedChanges => "SettingsWindow_FormUnsavedChanges",
            SettingsFormState.Applied => "SettingsWindow_FormApplied",
            _ => "SettingsWindow_FormNoChanges",
        };
}
