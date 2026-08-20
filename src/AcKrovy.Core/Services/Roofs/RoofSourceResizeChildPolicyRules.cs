using AcKrovy.Core.Services.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Source SupportedResize must win over incidental child STRETCH in the same command.
/// </summary>
public static class RoofSourceResizeChildPolicyRules
{
    public static bool ShouldIgnoreIncidentalChildStretchOnSourceResize(
        bool ownerHadSupportedResizeThisCommand,
        string? globalCommandName) =>
        ownerHadSupportedResizeThisCommand &&
        (RoofGeneratedMemberEditCommandRules.IsClassicStretch(globalCommandName) ||
         RoofGeneratedMemberEditCommandRules.IsGripStretchCommand(globalCommandName));
}
