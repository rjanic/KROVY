namespace AcKrovy.Core.Models;

/// <summary>
/// CAD-neutral text-style preset definition shared by built-in and user presets.
/// Built-ins carry a localization key and no display name; user presets carry a
/// user-entered display name and no localization key.
/// </summary>
public sealed record TimberAnnotationTextStylePresetDefinition(
    string StableId,
    TimberAnnotationTextStylePresetKind Kind,
    TimberAnnotationBuiltInTextStylePreset? BuiltInPreset,
    string? LocalizationKey,
    string? DisplayName,
    string AutoCadTextStyleName,
    string FontFile,
    double WidthFactor,
    double ObliqueAngleDegrees);
