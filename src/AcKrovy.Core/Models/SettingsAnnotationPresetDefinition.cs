namespace AcKrovy.Core.Models;

public sealed record SettingsAnnotationPresetDefinition(
    SettingsAnnotationPreset Preset,
    string StableId,
    string ReferenceFileName,
    string LocalizationKey,
    TimberAnnotationMode AnnotationMode,
    ItemNumberLeaderStyle ItemNumberLeaderStyle);
