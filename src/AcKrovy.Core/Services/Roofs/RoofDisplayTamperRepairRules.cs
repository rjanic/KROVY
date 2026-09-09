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

        // Locked roofs protect derived display against both transform tamper
        // and native ERASE.
        return editState == RoofEditState.Locked &&
               RoofGeneratedMemberEditCommandRules.IsGeneratedTimberEditCommand(
                   globalCommandName);
    }
}
