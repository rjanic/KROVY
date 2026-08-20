using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Maps roof edit permission to AutoCAD GROUP selectable flag.
/// Membership is unchanged; only group-level selection behavior toggles.
/// </summary>
public static class RoofDisplayGroupSelectabilityRules
{
    public static bool ShouldEnableGroupSelection(RoofEditState editState) =>
        editState == RoofEditState.Locked;
}
