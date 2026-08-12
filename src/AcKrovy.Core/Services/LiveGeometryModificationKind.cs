namespace AcKrovy.Core.Services;

/// <summary>
/// Classifies live geometry work after a host command so annotation-only
/// presentation edits do not enter the timber source refresh path.
/// </summary>
public enum LiveGeometryModificationKind
{
    None = 0,

    /// <summary>
    /// Owned annotation geometry/presentation changed while timber sources did
    /// not. Preserve the native manual edit; do not rebuild from source.
    /// </summary>
    AnnotationPresentationChanged = 1,

    /// <summary>
    /// One or more timber sources changed (or a full timber annotation refresh
    /// is required without a pure annotation-presentation signal).
    /// </summary>
    SourceGeometryChanged = 2,
}
