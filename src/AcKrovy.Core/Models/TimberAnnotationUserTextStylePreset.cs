namespace AcKrovy.Core.Models;

/// <summary>
/// Persisted shape for one user-defined annotation text-style preset.
/// <see cref="StableId"/> is the durable identity; the AutoCAD style name is
/// derived from it so renaming the display name does not create a new style.
/// </summary>
public sealed class TimberAnnotationUserTextStylePreset
{
    public string StableId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FontFile { get; set; } = string.Empty;
    public string AutoCadTextStyleName { get; set; } = string.Empty;
    public double WidthFactor { get; set; } = 1.0d;
    public double ObliqueAngleDegrees { get; set; } = 0.0d;
}
