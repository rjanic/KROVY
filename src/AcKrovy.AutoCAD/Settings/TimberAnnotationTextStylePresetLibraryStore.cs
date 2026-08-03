using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcKrovy.AutoCAD.Diagnostics;
using AcKrovy.Core.Models;
using AcKrovy.Infrastructure.Diagnostics;

namespace AcKrovy.AutoCAD.Settings;

/// <summary>
/// Persists the user-defined annotation text-style preset library outside the DWG.
/// Versioned independently from metadata schema and the timber default profile.
/// </summary>
internal static class TimberAnnotationTextStylePresetLibraryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static TimberAnnotationTextStylePresetLibrary Load() =>
        AcKrovyDiagnostics.Settings.Load(
            LocalSettingsPaths.TextStylePresets,
            SettingsConfigurationSubject.TextStylePresets,
            json => JsonSerializer
                .Deserialize<TimberAnnotationTextStylePresetLibrary>(json, JsonOptions)
                ?.Normalize(),
            TimberAnnotationTextStylePresetLibrary.CreateDefault).Value;

    public static void Save(TimberAnnotationTextStylePresetLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);

        var normalized = library.PrepareForWrite();
        AcKrovyDiagnostics.Settings.Save(
            LocalSettingsPaths.TextStylePresets,
            SettingsConfigurationSubject.TextStylePresets,
            JsonSerializer.Serialize(normalized, JsonOptions));
    }
}
