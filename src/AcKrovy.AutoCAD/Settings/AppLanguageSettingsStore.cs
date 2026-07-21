using System.IO;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.Settings;

/// <summary>Persists the global plug-in UI language outside every DWG.</summary>
internal static class AppLanguageSettingsStore
{
    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ACAD_KROVY");

    private static string SettingsPath => Path.Combine(SettingsDirectory, "application-settings.json");

    public static AppLanguageSettings Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? AppLanguageSettingsSerializer.Deserialize(File.ReadAllText(SettingsPath))
                : new AppLanguageSettings();
        }
        catch
        {
            return new AppLanguageSettings();
        }
    }

    public static void Save(AppLanguageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(SettingsDirectory);
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, AppLanguageSettingsSerializer.Serialize(settings));
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }
}
