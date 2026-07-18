using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcKrovy.Core.Models;

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

    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ACAD_KROVY");

    private static string SettingsPath => Path.Combine(SettingsDirectory, "timber-element-default-profile.json");

    public static TimberElementDefaultProfile Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return TimberElementDefaultProfile.CreateDefault();
            }

            var json = File.ReadAllText(SettingsPath);
            var profile = JsonSerializer.Deserialize<TimberElementDefaultProfile>(json, JsonOptions);
            return profile?.Normalize() ?? TimberElementDefaultProfile.CreateDefault();
        }
        catch
        {
            return TimberElementDefaultProfile.CreateDefault();
        }
    }

    public static void Save(TimberElementDefaultProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        Directory.CreateDirectory(SettingsDirectory);
        var normalized = profile.Normalize();
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(normalized, JsonOptions));
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }
}
