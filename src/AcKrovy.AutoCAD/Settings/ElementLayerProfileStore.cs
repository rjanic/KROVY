using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcKrovy.Cad.Abstractions.Layers;

namespace AcKrovy.AutoCAD.Settings;

/// <summary>Ukladá profil hladín bezpečne mimo DWG do AppData aktuálneho používateľa.</summary>
internal static class ElementLayerProfileStore
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

    private static string SettingsPath => Path.Combine(SettingsDirectory, "element-layer-profile.json");

    public static ElementLayerProfile Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return ElementLayerProfile.CreateDefault();
            }

            var json = File.ReadAllText(SettingsPath);
            var profile = JsonSerializer.Deserialize<ElementLayerProfile>(json, JsonOptions);
            return profile?.Normalize() ?? ElementLayerProfile.CreateDefault();
        }
        catch
        {
            // Poškodený lokálny profil nesmie zablokovať výkres. Použijeme bezpečné predvolené hodnoty.
            return ElementLayerProfile.CreateDefault();
        }
    }

    public static void Save(ElementLayerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        Directory.CreateDirectory(SettingsDirectory);
        var normalized = profile.Normalize();
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(normalized, JsonOptions));
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }
}
