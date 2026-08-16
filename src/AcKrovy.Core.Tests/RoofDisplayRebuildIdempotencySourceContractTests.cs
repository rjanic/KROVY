using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Guards the HOST-proven +7 entity / +1 group leak after display-tamper Rebuild:
/// erase must not require current owner-reference match, and EnsureGroup must
/// dissolve foreign AutoCAD groups that still hold the same semantic source.
/// </summary>
public sealed class RoofDisplayRebuildIdempotencySourceContractTests
{
    private static readonly string Display = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayService.cs");
    private static readonly string Group = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayGroupService.cs");

    [Fact]
    public void RebuildErase_UsesPortableRulesWithoutOwnerStringGate()
    {
        var rebuild = RoofUxSourceContractText.Member(
            Display,
            "public static bool Rebuild",
            "private static RoofPoint3D MapPoint");
        Assert.Contains("RoofDisplayRebuildEraseRules.ShouldEraseInspectedDisplayChild", rebuild);
        Assert.Contains("RoofDisplayRebuildEraseRules.ShouldEraseOwnerMatchedSweepChild", rebuild);
        Assert.Contains("CollectDisplayIdsToErase", rebuild);
        Assert.Contains("TryCollectStrictStructuralDisplayEraseIds", rebuild);
        Assert.Contains("TryCollectStrictStructuralDisplayEraseIds", Group);
        Assert.Contains("RoofDisplayForeignGroupEraseRules.TrySelectDisplayEraseMemberKeys", Group);
        Assert.DoesNotContain(
            "string.Equals(\r\n                    stored.OwnerReference,\r\n                    ownerReference,",
            rebuild);
        Assert.DoesNotContain(
            "string.Equals(\n                    stored.OwnerReference,\n                    ownerReference,",
            rebuild);
    }

    [Fact]
    public void RebuildCreatesExactlySevenDisplayChildrenThenEnsuresCanonicalGroup()
    {
        var rebuild = RoofUxSourceContractText.Member(
            Display,
            "public static bool Rebuild",
            "private static RoofPoint3D MapPoint");
        Assert.Contains("SimpleGableRoofWireframe.EdgeCount", rebuild);
        Assert.Contains("newChildIds", rebuild);
        Assert.Contains("RoofDisplayGroupService.EnsureGroup", rebuild);
        Assert.Contains("DissociateOwnerFromForeignGroups", Group);
    }

    [Fact]
    public void EnsureGroup_DissociatesOwnerFromForeignGroupsAfterCanonicalMembership()
    {
        Assert.Contains("DissociateOwnerFromForeignGroups", Group);
        Assert.Contains("group.Clear()", Group);
        Assert.Contains("dictionary.Remove(", Group);
        Assert.Contains("canonicalGroupName", Group);
    }

    [Fact]
    public void StrictForeignGroupErase_DoesNotUseGeometryColorLayerOrSpatialInference()
    {
        Assert.Contains("TryCollectStrictStructuralDisplayEraseIds", Group);
        Assert.DoesNotContain("ColorIndex", Group);
        Assert.DoesNotContain("GetClosestPointTo", Group);
        Assert.DoesNotContain("DistanceTo", Group);
        Assert.DoesNotContain("LayerName", Group);
    }

    [Fact]
    public void NoNewReactorOverruleOrDeepCloneArchitecture()
    {
        var source = Display + Group;
        Assert.DoesNotContain("DatabaseReactor", source);
        Assert.DoesNotContain("ObjectOverrule", source);
        Assert.DoesNotContain("DeepClone", source);
        Assert.DoesNotContain("IDeepClone", source);
        Assert.DoesNotContain("BeginDeepClone", source);
        Assert.DoesNotContain("EndDeepClone", source);
        Assert.DoesNotContain("IdMapping", source);
    }
}