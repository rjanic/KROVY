using AcKrovy.Core.Services;

namespace AcKrovy.Core.Services.Roofs;

public enum RoofOrdinaryGeometryAuthority
{
    RoofOwned,
    Independent,
}

/// <summary>The CAD representation through which a user attempted to edit geometry.</summary>
public enum RoofOrdinaryEditRepresentation
{
    Plan2D,
    Physical3D,
}

public enum RoofOrdinaryEditDecision
{
    NoTransition,
    PromptDetach,
    KeepIndependent,
    LockedRoof,
    RestoreDerivedPhysical,
}

/// <summary>Ownership and user edit representation are distinct authority decisions.</summary>
public static class RoofOrdinaryAuthorityTransitionRules
{
    public static RoofOrdinaryEditRepresentation UserGeometryEditAuthority =>
        RoofOrdinaryEditRepresentation.Plan2D;

    public static RoofOrdinaryEditDecision Decide(RoofOrdinaryGeometryAuthority authority,
        string? commandName, bool geometryChanged, bool sourceChanged, bool roofLocked,
        RoofOrdinaryEditRepresentation editRepresentation = RoofOrdinaryEditRepresentation.Plan2D)
    {
        if (editRepresentation == RoofOrdinaryEditRepresentation.Physical3D)
            return RoofOrdinaryEditDecision.RestoreDerivedPhysical;
        if (authority == RoofOrdinaryGeometryAuthority.Independent)
            return RoofOrdinaryEditDecision.KeepIndependent;
        if (!geometryChanged || sourceChanged ||
            !(RoofGeneratedMemberEditCommandRules.IsMoveCommand(commandName) ||
              RoofGeneratedMemberEditCommandRules.IsGripStretchCommand(commandName)))
            return RoofOrdinaryEditDecision.NoTransition;
        return roofLocked ? RoofOrdinaryEditDecision.LockedRoof :
            RoofOrdinaryEditDecision.PromptDetach;
    }
}
