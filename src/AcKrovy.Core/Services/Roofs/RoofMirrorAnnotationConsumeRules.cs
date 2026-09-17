namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Whole-roof / member MIRROR annotation consume identity.
/// Native MIRROR appends annotation clones that inherit the SOURCE timber handle.
/// Those clones must be erased. AutoCAD may also erase+recreate a source MLeader
/// during the same command; the recreated entity is appended with the same handle
/// but is the sole survivor and must be kept. Discrimination is therefore:
/// erase an appended annotation when a living non-appended peer still owns the
/// same SourceHandle + annotation role; when only appended survivors remain for a
/// role (source recreate, optionally plus a clone), keep exactly one.
/// </summary>
public static class RoofMirrorAnnotationConsumeRules
{
    public const string RoleMainLabelPrefix = "main-label:";
    public const string RoleSlopeArrow = "slope-arrow";
    public const string RoleSlopeAngle = "slope-angle";
    public const string RolePostPerpendicular = "post-perpendicular";

    public static string FormatMainLabelRole(string componentRole)
    {
        if (string.IsNullOrWhiteSpace(componentRole))
        {
            return RoleMainLabelPrefix + "unknown";
        }

        return RoleMainLabelPrefix + componentRole.Trim();
    }

    /// <summary>
    /// Returns true when the appended entity is a disposable MIRROR clone because a
    /// living non-appended peer still owns the same role.
    /// </summary>
    public static bool ShouldEraseAppendedAnnotationClone(bool hasLivingNonAppendedPeerSameRole) =>
        hasLivingNonAppendedPeerSameRole;

    /// <summary>
    /// When no non-appended peer remains, keep exactly one appended survivor per role
    /// (prefer lowest handle) and erase surplus appended duplicates.
    /// </summary>
    public static bool ShouldKeepSoleAppendedSurvivor(
        long handleValue,
        long lowestHandleValueAmongAppendedSameRole) =>
        handleValue == lowestHandleValueAmongAppendedSameRole;

    public static string ClassifyAppendedAnnotation(
        bool hasLivingNonAppendedPeerSameRole) =>
        hasLivingNonAppendedPeerSameRole ? "mirrored-clone" : "source-original";

    public static string ActionForAppendedAnnotation(
        bool hasLivingNonAppendedPeerSameRole) =>
        hasLivingNonAppendedPeerSameRole ? "erase" : "keep";

    /// <summary>
    /// Origin-annotation invariant baseline must count only pre-existing source
    /// annotations. Native MIRROR clones share SourceHandle with the origin but are
    /// ObjectAppended in this command and must be excluded from beforeCount.
    /// </summary>
    public static IReadOnlyList<string> SelectOriginAnnotationKeysExcludingAppended(
        IReadOnlyList<RoofMirrorOriginAnnotationCandidate> candidates,
        IReadOnlyCollection<string> originTimberSourceHandles,
        IReadOnlyCollection<string> appendedThisCommandAnnotationKeys)
    {
        if (candidates is null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }

        if (originTimberSourceHandles is null)
        {
            throw new ArgumentNullException(nameof(originTimberSourceHandles));
        }

        if (appendedThisCommandAnnotationKeys is null)
        {
            throw new ArgumentNullException(nameof(appendedThisCommandAnnotationKeys));
        }

        var timber = new HashSet<string>(
            originTimberSourceHandles
                .Where(handle => !string.IsNullOrWhiteSpace(handle))
                .Select(handle => handle.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var appended = new HashSet<string>(
            appendedThisCommandAnnotationKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => key.Trim()),
            StringComparer.OrdinalIgnoreCase);

        return candidates
            .Where(candidate =>
                !string.IsNullOrWhiteSpace(candidate.AnnotationKey) &&
                !string.IsNullOrWhiteSpace(candidate.SourceHandle) &&
                timber.Contains(candidate.SourceHandle.Trim()) &&
                !appended.Contains(candidate.AnnotationKey.Trim()))
            .Select(candidate => candidate.AnnotationKey.Trim())
            .ToArray();
    }
}

/// <summary>Portable candidate for origin-annotation invariant baseline filtering.</summary>
public sealed class RoofMirrorOriginAnnotationCandidate
{
    public string AnnotationKey { get; init; } = string.Empty;
    public string SourceHandle { get; init; } = string.Empty;
}
