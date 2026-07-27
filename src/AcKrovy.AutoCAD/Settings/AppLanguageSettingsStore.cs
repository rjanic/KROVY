using System.Globalization;
using System.IO;
using System.Text.Json;
using AcKrovy.AutoCAD.Diagnostics;
using AcKrovy.Infrastructure.Diagnostics;
using AcKrovy.Infrastructure.Settings;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.Settings;

/// <summary>Persists the global plug-in UI language outside every DWG.</summary>
internal static class AppLanguageSettingsStore
{
    public static AppLanguageSettings Load()
    {
        var result = AcKrovyDiagnostics.Settings.Load(
            LocalSettingsPaths.ApplicationSettings,
            SettingsConfigurationSubject.ApplicationLanguage,
            DeserializeStrict,
            () => new AppLanguageSettings());

        return result.Status.State == SettingsFileState.Missing
            ? new AppLanguageSettings
            {
                LanguageCode = AppLanguageService.ResolveFirstRunLanguageCode(
                    CultureInfo.InstalledUICulture),
            }
            : result.Value;
    }

    public static void Save(AppLanguageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        AcKrovyDiagnostics.Settings.Save(
            LocalSettingsPaths.ApplicationSettings,
            SettingsConfigurationSubject.ApplicationLanguage,
            AppLanguageSettingsSerializer.Serialize(settings));
    }

    private static AppLanguageSettings DeserializeStrict(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Application settings root must be a JSON object.");
        }

        return AppLanguageSettingsSerializer.Deserialize(json);
    }
}
