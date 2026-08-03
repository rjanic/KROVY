using System.IO;

namespace AcKrovy.AutoCAD.Settings;

internal static class LocalSettingsPaths
{
    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ACAD_KROVY");

    public static string ApplicationSettings =>
        Path.Combine(DirectoryPath, "application-settings.json");

    public static string UiPreferences =>
        Path.Combine(DirectoryPath, "settings-ui.json");

    public static string LayerProfile =>
        Path.Combine(DirectoryPath, "element-layer-profile.json");

    public static string TimberDefaults =>
        Path.Combine(DirectoryPath, "timber-element-default-profile.json");

    public static string CustomDefinitions =>
        Path.Combine(DirectoryPath, "custom-element-definitions.json");

    public static string TextStylePresets =>
        Path.Combine(DirectoryPath, "annotation-text-style-presets.json");
}
