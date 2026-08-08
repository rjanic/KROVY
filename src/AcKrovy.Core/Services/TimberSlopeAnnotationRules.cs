using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberSlopeAnnotationRules
{
    public static TimberSlopeGlyphKind ResolveGlyphKind(
        TimberElementType elementType,
        double slopeDegrees) =>
        elementType == TimberElementType.Post
            ? TimberSlopeGlyphKind.PostPerpendicularMarker
            : ResolveGlyphKind(slopeDegrees);

    public static TimberSlopeGlyphKind ResolveGlyphKind(double slopeDegrees)
    {
        if (!TimberCalculator.IsValidSlopeDegrees(slopeDegrees))
        {
            return TimberSlopeGlyphKind.None;
        }

        return slopeDegrees == 0d
            ? TimberSlopeGlyphKind.HorizontalMarker
            : TimberSlopeGlyphKind.DirectionalArrow;
    }

    public static bool ShouldDisplayAngleText(double slopeDegrees) =>
        TimberCalculator.IsValidSlopeDegrees(slopeDegrees);

    public static bool ShouldDisplayAngleText(TimberElementType elementType, double slopeDegrees) =>
        elementType == TimberElementType.Post || ShouldDisplayAngleText(slopeDegrees);

    public static double ResolveDisplayAngleDegrees(TimberElementType elementType, double slopeDegrees) =>
        elementType == TimberElementType.Post
            ? TimberPostAnnotationGeometryCalculator.DisplayAngleDegrees
            : slopeDegrees;

    public static bool CanFlipDirection(double slopeDegrees) =>
        ResolveGlyphKind(slopeDegrees) == TimberSlopeGlyphKind.DirectionalArrow;

    public static bool CanFlipDirection(TimberElementType elementType, double slopeDegrees) =>
        ResolveGlyphKind(elementType, slopeDegrees) == TimberSlopeGlyphKind.DirectionalArrow;

    public static bool ToggleDirection(bool isSlopeDirectionReversed) =>
        !isSlopeDirectionReversed;

    public static bool HasSameSourceHandle(string? annotationSourceHandle, string? timberSourceHandle)
    {
        if (string.IsNullOrWhiteSpace(annotationSourceHandle) || string.IsNullOrWhiteSpace(timberSourceHandle))
        {
            return false;
        }

        return string.Equals(
            annotationSourceHandle!.Trim(),
            timberSourceHandle!.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Roles whose geometry may drive slope longitudinal clearance. G4
    /// <see cref="TimberMainAnnotationComponentRole.CircleLeaderLine"/>
    /// (NoneContent) and frame-only block refs are excluded — they are not
    /// text annotations and must not be probed via MLeader.TextLocation.
    /// </summary>
    public static bool IsLongitudinalIntervalLabelRole(
        TimberMainAnnotationComponentRole role) =>
        role is
            TimberMainAnnotationComponentRole.Primary or
            TimberMainAnnotationComponentRole.FramedItem or
            TimberMainAnnotationComponentRole.CircleText;
}
