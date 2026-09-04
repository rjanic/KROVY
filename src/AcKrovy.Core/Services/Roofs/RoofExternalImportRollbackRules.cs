namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// CAD-neutral C1 abort absence for appended clone identities after an uncommitted
/// insert into the target drawing. Real imported content must be invalid or erased.
/// Host databases may also append structural block-definition delimiters owned by a
/// newly created block table record; after abort those delimiters may remain
/// non-erased while their owning record is erased. They count as absent only under
/// that structural-owner condition — never via a broad owner-erased rule for
/// ordinary entities.
/// </summary>
public static class RoofExternalImportRollbackRules
{
    /// <summary>
    /// Returns whether one appended identity is rollback-absent for
    /// <c>importedObjectsAbsent</c>.
    /// </summary>
    /// <param name="objectIsValid">Host identity validity observation.</param>
    /// <param name="objectIsErased">Host erased observation.</param>
    /// <param name="isStructuralBlockTableRecordSentinel">
    /// True only when the host classifies the object as a structural
    /// block-definition entity-list delimiter.
    /// </param>
    /// <param name="ownerIsValid">Owner validity when classified as a sentinel.</param>
    /// <param name="ownerIsErased">Owner erased when classified as a sentinel.</param>
    public static bool IsAppendedObjectAbsent(
        bool objectIsValid,
        bool objectIsErased,
        bool isStructuralBlockTableRecordSentinel,
        bool ownerIsValid,
        bool ownerIsErased)
    {
        if (!objectIsValid || objectIsErased)
        {
            return true;
        }

        if (!isStructuralBlockTableRecordSentinel)
        {
            return false;
        }

        return !ownerIsValid || ownerIsErased;
    }
}
