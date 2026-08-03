using AcKrovy.Core.Services;

namespace AcKrovy.Core.Models;

/// <summary>
/// Versioned local library of user-defined annotation text-style presets.
/// Built-in presets are not stored here; they live in
/// <see cref="TimberAnnotationTextStylePresetRules"/>.
/// </summary>
public sealed class TimberAnnotationTextStylePresetLibrary
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<TimberAnnotationUserTextStylePreset> Presets { get; set; } = new();

    public TimberAnnotationTextStylePresetLibrary Normalize() =>
        TimberAnnotationTextStylePresetRules.NormalizeLibrary(this);

    /// <summary>
    /// Mirrors the profile rule that a stored version is upgraded only when the
    /// library is written back, never during a load.
    /// </summary>
    public TimberAnnotationTextStylePresetLibrary PrepareForWrite()
    {
        var normalized = Normalize();
        normalized.Version = CurrentVersion;
        return normalized;
    }

    public static TimberAnnotationTextStylePresetLibrary CreateDefault() => new()
    {
        Version = CurrentVersion,
        Presets = new List<TimberAnnotationUserTextStylePreset>(),
    };
}
