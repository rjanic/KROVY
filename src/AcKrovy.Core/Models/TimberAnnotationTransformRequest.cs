namespace AcKrovy.Core.Models;

/// <summary>
/// Manual annotation presentation transform kinds for the future Edit kóty
/// feature. These never describe timber source geometry edits.
/// </summary>
public enum TimberAnnotationTransformKind
{
    /// <summary>Rotate relative to the current live content world angle.</summary>
    RotateRelative = 0,

    /// <summary>Set an absolute WCS content world orientation.</summary>
    SetWorldOrientation = 1,

    /// <summary>
    /// Reflect annotation presentation across the timber source axis through
    /// the live attachment. Does not describe glyph mirroring or negative scale.
    /// </summary>
    MirrorAcrossSourceAxis = 2,
}

/// <summary>
/// CAD-neutral request for one manual annotation presentation transform.
/// Angles are WCS radians, positive counterclockwise about +Z.
/// </summary>
public sealed record TimberAnnotationTransformRequest
{
    private TimberAnnotationTransformRequest(
        TimberAnnotationTransformKind kind,
        double? angleRadians)
    {
        Kind = kind;
        AngleRadians = angleRadians;
    }

    public TimberAnnotationTransformKind Kind { get; }

    /// <summary>
    /// Required for <see cref="TimberAnnotationTransformKind.RotateRelative"/>
    /// and <see cref="TimberAnnotationTransformKind.SetWorldOrientation"/>.
    /// Null for mirror.
    /// </summary>
    public double? AngleRadians { get; }

    public static TimberAnnotationTransformRequest RotateRelative(
        double angleRadians)
    {
        ValidateFinite(angleRadians, nameof(angleRadians));
        return new(
            TimberAnnotationTransformKind.RotateRelative,
            angleRadians);
    }

    public static TimberAnnotationTransformRequest SetWorldOrientation(
        double angleRadians)
    {
        ValidateFinite(angleRadians, nameof(angleRadians));
        return new(
            TimberAnnotationTransformKind.SetWorldOrientation,
            angleRadians);
    }

    /// <summary>Absolute WCS content orientation 0° (idempotent target).</summary>
    public static TimberAnnotationTransformRequest Horizontal() =>
        SetWorldOrientation(0d);

    /// <summary>
    /// Absolute WCS content orientation +90° (bottom-to-top text direction).
    /// </summary>
    public static TimberAnnotationTransformRequest Vertical() =>
        SetWorldOrientation(Math.PI / 2d);

    public static TimberAnnotationTransformRequest MirrorAcrossSourceAxis() =>
        new(TimberAnnotationTransformKind.MirrorAcrossSourceAxis, null);

    private static void ValidateFinite(double value, string paramName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, null);
        }
    }
}
