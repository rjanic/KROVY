using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcKrovy.AutoCAD.Diagnostics;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Infrastructure.Diagnostics;

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

    public static ElementLayerProfile Load() =>
        AcKrovyDiagnostics.Settings.Load(
            LocalSettingsPaths.LayerProfile,
            SettingsConfigurationSubject.LayerProfile,
            json => JsonSerializer.Deserialize<ElementLayerProfile>(json, JsonOptions)?.Normalize(),
            ElementLayerProfile.CreateDefault).Value;

    public static void Save(ElementLayerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var normalized = profile.Normalize();
        AcKrovyDiagnostics.Settings.Save(
            LocalSettingsPaths.LayerProfile,
            SettingsConfigurationSubject.LayerProfile,
            JsonSerializer.Serialize(normalized, JsonOptions));
    }
}
