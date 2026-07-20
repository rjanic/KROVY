using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberSlopeAnnotationRules
{
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

    public static bool CanFlipDirection(double slopeDegrees) =>
        ResolveGlyphKind(slopeDegrees) == TimberSlopeGlyphKind.DirectionalArrow;

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
}
