using System.IO;
using System.Text.Json;
using AcKrovy.AutoCAD.Diagnostics;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Infrastructure.Diagnostics;

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

    public static IReadOnlyList<CustomElementDefinition> Load() =>
        AcKrovyDiagnostics.Settings.Load(
            LocalSettingsPaths.CustomDefinitions,
            SettingsConfigurationSubject.CustomElementDefinitions,
            json =>
            {
                var definitions = JsonSerializer.Deserialize<List<CustomElementDefinition>>(
                    json,
                    JsonOptions);
                return definitions is null
                    ? null
                    : CustomElementDefinitionCatalogRules.Normalize(definitions);
            },
            () => Array.Empty<CustomElementDefinition>()).Value;

    public static void Save(IEnumerable<CustomElementDefinition> definitions)
    {
        var normalized = CustomElementDefinitionCatalogRules.Normalize(definitions);
        AcKrovyDiagnostics.Settings.Save(
            LocalSettingsPaths.CustomDefinitions,
            SettingsConfigurationSubject.CustomElementDefinitions,
            JsonSerializer.Serialize(normalized, JsonOptions));
    }
}
