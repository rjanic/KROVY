using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Decides when a modified derived roof-display entity must be restored from its
/// semantic source. The established STRETCH/grip repair remains available in both
/// edit states; Locked roofs additionally reject direct native member edits.
/// </summary>
public static class RoofDisplayTamperRepairRules
{
    public static bool ShouldRepair(
        RoofEditState editState,
        string? globalCommandName)
    {
        if (LiveGeometryCommandRules.IsUndoGroupingSourceCommand(globalCommandName))
        {
            return true;
        }

        // ERASE does not leave a readable ObjectModified display candidate from which
        // the existing owner resolver can recover the roof. Its lifecycle remains a
        // separate feature rather than guessing ownership after erasure.
        return editState == RoofEditState.Locked &&
               !RoofGeneratedMemberEditCommandRules.IsEraseCommand(globalCommandName) &&
               RoofGeneratedMemberEditCommandRules.IsGeneratedTimberEditCommand(
                   globalCommandName);
    }
}
