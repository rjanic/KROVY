namespace AcKrovy.Core.Services;

/// <summary>
/// Neutral classification of live CommandEnded work.
/// Distinguishes timber source geometry edits from owned-annotation-only
/// presentation edits (classic MOVE/ROTATE of a label).
/// </summary>
public static class LiveGeometryModificationClassifier
{
    public static LiveGeometryModificationKind Classify(
        int modifiedTimberSourceCount,
        int modifiedAnnotationPresentationCount,
        int appendedTimberCount,
        int erasedSourceHandleCount,
        bool requiresFullTimberAnnotationRefresh)
    {
        var sourceTouched =
            modifiedTimberSourceCount > 0 ||
            appendedTimberCount > 0 ||
            erasedSourceHandleCount > 0;

        if (sourceTouched)
        {
            return LiveGeometryModificationKind.SourceGeometryChanged;
        }

        // Annotation-only ROTATE/MOVE must not inherit the ROTATE full-refresh
        // fallback flag. That flag only matters when no timber/annotation signal
        // was observed (legacy ROTATE safety net).
        if (modifiedAnnotationPresentationCount > 0)
        {
            return LiveGeometryModificationKind.AnnotationPresentationChanged;
        }

        if (requiresFullTimberAnnotationRefresh)
        {
            return LiveGeometryModificationKind.SourceGeometryChanged;
        }

        return LiveGeometryModificationKind.None;
    }

    public static bool ShouldRunSourceCanonicalRefresh(LiveGeometryModificationKind kind) =>
        kind == LiveGeometryModificationKind.SourceGeometryChanged;

    public static bool ShouldPreserveAnnotationPresentationOnly(LiveGeometryModificationKind kind) =>
        kind == LiveGeometryModificationKind.AnnotationPresentationChanged;
}
