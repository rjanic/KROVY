using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// HOST-proven: after GROUP COPY, display-tamper Rebuild must erase the seven
/// stale-owner Lines still structurally grouped with the copied source (*A1).
/// </summary>
public sealed class RoofDisplayForeignGroupEraseSourceContractTests
{
    private static readonly string Display = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayService.cs");
    private static readonly string Group = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayGroupService.cs");
    private static readonly string CoreRules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofDisplayForeignGroupEraseRules.cs");
    private static readonly string Resize = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string Rehydration = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedRafterCopyOwnershipRehydrationService.cs");
    private static readonly string RafterSet = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedRafterSetService.cs");

    [Fact]
    public void CollectErase_UnionsInspectedOwnerMatchedAndStrictForeignGroup()
    {
        var collect = RoofUxSourceContractText.Member(
            Display,
            "private static List<ObjectId> CollectDisplayIdsToErase",
            "private static RoofPoint3D MapPoint");
        Assert.Contains("inspectedChildIds", collect);
        Assert.Contains("ShouldEraseOwnerMatchedSweepChild", collect);
        Assert.Contains("TryCollectStrictStructuralDisplayEraseIds", collect);
        Assert.Contains("eraseIds.Add", collect);
    }

    [Fact]
    public void StrictForeignGroup_IgnoresStaleOwnerAnd1005ForEraseEligibility()
    {
        Assert.Contains("TrySelectDisplayEraseMemberKeys", CoreRules);
        Assert.Contains("TryResolveUniqueEraseMemberKeys", CoreRules);
        Assert.DoesNotContain("OwnerReference", CoreRules);
        Assert.DoesNotContain("EffectiveOwner", Group);
        var collectStrict = RoofUxSourceContractText.Member(
            Group,
            "public static bool TryCollectStrictStructuralDisplayEraseIds",
            "private static bool TryBuildForeignGroupObservations");
        Assert.DoesNotContain("OwnerReference", collectStrict);
        Assert.DoesNotContain("ExtendedDataHandle", collectStrict);
        Assert.DoesNotContain("DxfOwnerHandleCode", collectStrict);
    }

    [Fact]
    public void StrictForeignGroup_RequiresSourceInGroupAndSevenUniqueRoles()
    {
        Assert.Contains("members.Contains(ownerId)", Group);
        Assert.Contains("SimpleGableRoofWireframe.EdgeCount", CoreRules);
        Assert.Contains("RoofDisplayEdgeRole.Ridge", CoreRules);
        Assert.Contains("RoofDisplayEdgeRole.Eave0", CoreRules);
        Assert.Contains("RoofDisplayEdgeRole.Eave1", CoreRules);
        Assert.Contains("RoofDisplayEdgeRole.GableSlope00", CoreRules);
        Assert.Contains("RoofDisplayEdgeRole.GableSlope01", CoreRules);
        Assert.Contains("RoofDisplayEdgeRole.GableSlope10", CoreRules);
        Assert.Contains("RoofDisplayEdgeRole.GableSlope11", CoreRules);
        Assert.Contains("RoofDisplayForeignGroupMemberKind.Other", CoreRules);
    }

    [Fact]
    public void DisplayTamperClassificationAndNotificationsUnchanged()
    {
        Assert.Contains("RoofSourceChangeKind.RigidEquivalent", Resize);
        Assert.Contains("Command_Roof_DisplayTamperNotificationTitle", Resize);
        Assert.Contains("ApplyDisplayTampers", Resize);
        Assert.DoesNotContain("TryCollectStrictStructuralDisplayEraseIds", Resize);
    }

    [Fact]
    public void CopyRafterOwnershipAndRegenerationUntouchedByForeignErase()
    {
        Assert.DoesNotContain("TryCollectStrictStructuralDisplayEraseIds", Rehydration);
        Assert.DoesNotContain("TryCollectStrictStructuralDisplayEraseIds", RafterSet);
        Assert.DoesNotContain("RoofDisplayForeignGroupEraseRules", Rehydration);
        Assert.DoesNotContain("RoofDisplayForeignGroupEraseRules", RafterSet);
    }

    [Fact]
    public void NoReactorDeepCloneOrGlobalOrphanSweeper()
    {
        var source = Display + Group + CoreRules;
        Assert.DoesNotContain("BeginDeepClone", source);
        Assert.DoesNotContain("EndDeepClone", source);
        Assert.DoesNotContain("IdMapping", source);
        Assert.DoesNotContain("DatabaseReactor", source);
        Assert.DoesNotContain("GlobalOrphan", source);
        Assert.DoesNotContain("EraseAllStale", source);
    }
}
