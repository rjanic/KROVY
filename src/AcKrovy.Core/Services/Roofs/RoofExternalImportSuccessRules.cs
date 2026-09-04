namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// CAD-neutral C2 success presence for imported identities after a committed
/// target transaction. Real imported content must remain valid and non-erased.
/// Structural block-definition delimiters never count as semantic imported
/// content. Reused support definitions are not new content.
/// </summary>
public static class RoofExternalImportSuccessRules
{
    /// <summary>
    /// Returns whether one identity counts as present real imported content for
    /// <c>importedObjectsPresent</c>. Structural sentinels never contribute.
    /// </summary>
    public static bool IsRealImportedObjectPresent(
        bool objectIsValid,
        bool objectIsErased,
        bool isStructuralBlockTableRecordSentinel)
    {
        if (isStructuralBlockTableRecordSentinel)
        {
            return false;
        }

        return objectIsValid && !objectIsErased;
    }

    /// <summary>
    /// Returns whether a mapped destination participates in the new-content
    /// survival set. Reused support identities (not cloned) are excluded.
    /// </summary>
    public static bool IsNewImportedContentCandidate(
        bool sourceIsExpectedClone,
        bool isCloned,
        bool isSupportBlockTableRecord)
    {
        if (!sourceIsExpectedClone)
        {
            return false;
        }

        if (isSupportBlockTableRecord && !isCloned)
        {
            return false;
        }

        return isCloned;
    }

    /// <summary>
    /// Overall C2 object-presence verdict: every required new-content candidate
    /// must be present; structural sentinels never satisfy the set.
    /// </summary>
    public static bool AreImportedObjectsPresent(
        int requiredNewContentCount,
        int presentRealContentCount,
        int structuralSentinelCount)
    {
        _ = structuralSentinelCount;
        return requiredNewContentCount > 0 &&
               presentRealContentCount == requiredNewContentCount;
    }
}
