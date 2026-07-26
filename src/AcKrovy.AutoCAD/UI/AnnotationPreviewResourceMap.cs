using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.UI;

internal static class AnnotationPreviewResourceMap
{
    private const string PackBase =
        "pack://application:,,,/AcKrovy.AutoCAD;component/UI/Assets/AnnotationPreviews/";

    public static string GetPackUri(SettingsAnnotationPreset preset) =>
        PackBase + SettingsAnnotationPresetRules.Get(preset).ReferenceFileName;

    public static IReadOnlyList<string> AllPackUris { get; } =
        SettingsAnnotationPresetRules.All
            .Select(definition => PackBase + definition.ReferenceFileName)
            .ToArray();
}
