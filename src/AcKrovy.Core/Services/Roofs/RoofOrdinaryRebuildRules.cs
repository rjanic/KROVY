using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

public static class RoofOrdinaryRebuildRules
{
    /// <summary>One generator replay plan for edit preview and authoritative Apply.
    /// Live entity inventory and Independent members are deliberately not inputs.</summary>
    public static RoofGeneratedMemberReplayPlan CreateReplayPlan(RoofRafterLayout layout, RoofDefinitionData? definition)
    {
        var prepared = definition is null ? null : Prepare(definition);
        return RoofAcceptedOrdinaryOverrideReplayRules.Apply(
            RoofGeneratedMemberReplayPlanner.Create(layout, 0d,
                RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, prepared?.Overrides),
            prepared?.EditState ?? RoofEditState.Locked);
    }

    /// <summary>Legacy exclusion remains decodable, but is not a current AUTO generator input.</summary>
    public static RoofDefinitionData Prepare(RoofDefinitionData definition) => definition with
    { ManualOverrides = definition.Overrides.Where(item => !item.Suppressed || item.Key.MemberKind != RoofGeneratedTimberKind.Rafter).ToArray() };
}
