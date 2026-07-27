using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcKrovy.AutoCAD.Diagnostics;
using AcKrovy.Core.Models;
using AcKrovy.Infrastructure.Diagnostics;

namespace AcKrovy.AutoCAD.Settings;

/// <summary>Ukladá používateľské predvolené výrobné hodnoty mimo DWG do AppData.</summary>
internal static class TimberElementDefaultProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static TimberElementDefaultProfile Load() =>
        AcKrovyDiagnostics.Settings.Load(
            LocalSettingsPaths.TimberDefaults,
            SettingsConfigurationSubject.TimberDefaults,
            json => JsonSerializer.Deserialize<TimberElementDefaultProfile>(json, JsonOptions)?.Normalize(),
            TimberElementDefaultProfile.CreateDefault).Value;

    public static void Save(TimberElementDefaultProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var normalized = profile.Normalize();
        AcKrovyDiagnostics.Settings.Save(
            LocalSettingsPaths.TimberDefaults,
            SettingsConfigurationSubject.TimberDefaults,
            JsonSerializer.Serialize(normalized, JsonOptions));
    }
}
