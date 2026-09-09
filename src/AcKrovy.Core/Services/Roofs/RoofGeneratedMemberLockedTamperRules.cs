using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// CAD-neutral classification for Locked generated-timber / annotation tamper vs
/// authoritative source resize. Host Inspect maps entities onto these outcomes.
/// </summary>
public static class RoofGeneratedMemberLockedTamperRules
{
    public enum Classification
    {
        SupportedSourceResize = 0,
        LockedGeneratedTamper = 1,
        LockedAnnotationTamper = 2,
        IgnoreUndoRedo = 3,
        Unsupported = 4,
        NotApplicable = 5,
    }

    /// <summary>
    /// Generated-child MOVE/ROTATE/SCALE/STRETCH/grip is Locked tamper only when the
    /// authoritative roof source was not part of the same native edit.
    /// </summary>
    public static Classification Classify(
        RoofEditState editState,
        string? globalCommandName,
        bool sourceModified,
        bool generatedMemberModified,
        bool ownedAnnotationModified,
        RoofSourceChangeKind sourceKind)
    {
        if (LiveGeometryCommandRules.IsUndoRedoCommand(globalCommandName))
        {
            return Classification.IgnoreUndoRedo;
        }

        if (sourceModified &&
            sourceKind == RoofSourceChangeKind.SupportedResize)
        {
            return Classification.SupportedSourceResize;
        }

        if (sourceModified)
        {
            return sourceKind == RoofSourceChangeKind.Unsupported
                ? Classification.Unsupported
                : Classification.NotApplicable;
        }

        if (editState != RoofEditState.Locked ||
            !RoofGeneratedMemberEditCommandRules.IsGeneratedTimberEditCommand(globalCommandName))
        {
            return Classification.NotApplicable;
        }

        if (generatedMemberModified)
        {
            return Classification.LockedGeneratedTamper;
        }

        if (ownedAnnotationModified)
        {
            return Classification.LockedAnnotationTamper;
        }

        return Classification.NotApplicable;
    }

    public static bool ShouldQueueLockedGeneratedRecovery(
        RoofEditState editState,
        string? globalCommandName,
        bool sourceModified,
        bool generatedOrAnnotationModified,
        RoofSourceChangeKind sourceKind)
    {
        if (!generatedOrAnnotationModified ||
            sourceKind is not (
                RoofSourceChangeKind.RigidEquivalent or
                RoofSourceChangeKind.SupportedResize))
        {
            return false;
        }

        // generatedOrAnnotationModified collapses timber+annotation signals for host
        // queue gates. Classify with timber=true to accept either LockedGeneratedTamper
        // or LockedAnnotationTamper (annotation-only uses the same recovery pipeline).
        var classification = Classify(
            editState,
            globalCommandName,
            sourceModified,
            generatedMemberModified: true,
            ownedAnnotationModified: false,
            sourceKind);
        return classification is
            Classification.LockedGeneratedTamper or
            Classification.LockedAnnotationTamper;
    }

    /// <summary>
    /// Unlocked annotation-only presentation edits stay on the classic
    /// PersistFramedManualOffsets path and must not enter generated-member ProcessOwners.
    /// </summary>
    public static bool ShouldDeferUnlockedAnnotationPresentationOnly(
        RoofEditState editState,
        bool generatedMemberModified,
        bool ownedAnnotationModified) =>
        editState == RoofEditState.Unlocked &&
        !generatedMemberModified &&
        ownedAnnotationModified;
}
