using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Pure rules for Locked ERASE recovery using a pre-command handle → owner map.
/// Locked source footprint ERASE is restored; Unlocked intentional source deletion
/// remains unrepaired.
/// </summary>
public static class RoofDisplayErasePreCommandMapRules
{
    public static bool ShouldRestoreLockedSourceErase(
        RoofEditState editStateAtStart,
        string? globalCommandName)
    {
        if (LiveGeometryCommandRules.IsUndoRedoCommand(globalCommandName) ||
            !RoofGeneratedMemberEditCommandRules.IsEraseCommand(globalCommandName))
        {
            return false;
        }

        return editStateAtStart == RoofEditState.Locked;
    }

    public static bool ShouldClassifyLockedDisplayEraseTamper(
        RoofEraseMappedKind mappedKind,
        bool sourceErasedInSameCommand,
        RoofEditState editState,
        string? globalCommandName)
    {
        // When Locked source is erased in the same command, source recovery owns the
        // full roof (source unerase + display reconcile). Display-only path stays for
        // display children erased without their Locked source.
        if (mappedKind != RoofEraseMappedKind.Display || sourceErasedInSameCommand)
        {
            return false;
        }

        return RoofDisplayTamperRepairRules.ShouldRepair(editState, globalCommandName);
    }

    /// <summary>
    /// Unlocked source ERASE remains intentional deletion (no Locked resurrection).
    /// </summary>
    public static bool IsUnlockedIntentionalSourceDeletion(
        RoofEditState editStateAtStart,
        RoofEraseMappedKind mappedKind) =>
        editStateAtStart == RoofEditState.Unlocked &&
        mappedKind == RoofEraseMappedKind.Source;
}
