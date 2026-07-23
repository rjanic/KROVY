using System.IO;
using System.Text.Json;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.Settings;

/// <summary>
/// Optional per-user catalog for reusing definitions. Every assigned entity
/// still stores a complete definition in DWG metadata.
/// </summary>
internal static class CustomElementDefinitionCatalogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ACAD_KROVY");

    private static string SettingsPath =>
        Path.Combine(SettingsDirectory, "custom-element-definitions.json");

    public static IReadOnlyList<CustomElementDefinition> Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return [];
            }

            var definitions = JsonSerializer.Deserialize<List<CustomElementDefinition>>(
                File.ReadAllText(SettingsPath),
                JsonOptions);
            return CustomElementDefinitionCatalogRules.Normalize(definitions);
        }
        catch
        {
            return [];
        }
    }

    public static void Save(IEnumerable<CustomElementDefinition> definitions)
    {
        var normalized = CustomElementDefinitionCatalogRules.Normalize(definitions);
        Directory.CreateDirectory(SettingsDirectory);
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(normalized, JsonOptions));
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }
}
