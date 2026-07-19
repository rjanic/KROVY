namespace AcKrovy.Core.Services;

public static class TimberSlopeAnnotationRules
{
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
