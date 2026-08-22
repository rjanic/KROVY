using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofWholeRoofCopyIdentityRulesTests
{
    private static RoofDefinitionData Definition(
        double slope = 35d,
        RoofKind kind = RoofKind.SimpleGable,
        double? face1Slope = null,
        double eaveHeightDifferenceMm = 0d,
        RoofEditState editState = RoofEditState.Locked,
        IReadOnlyList<RoofGeneratedMemberOverride>? overrides = null) =>
        new(
            RoofDefinitionDataSchema.CurrentVersion,
            kind,
            slope,
            RidgeDirectionX: 1d,
            RidgeDirectionY: 0d,
            FootprintSignature: "SIG-A",
            RidgeEdgeFamily: RoofRidgeEdgeFamily.SourceEdge01,
            RigidFootprint: new RoofRigidFootprintDescriptor(
                4,
                RoofPolygonOrientation.Clockwise,
                12000d,
                8000d),
            editState,
            overrides,
            face1Slope,
            eaveHeightDifferenceMm);

    private static RoofGeneratedMemberOverride Override(
        bool suppressed = false,
        string? reservedElementId = null) =>
        new(
            new RoofGeneratedMemberKey(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 3),
            suppressed,
            AlongMm: 0d,
            LateralMm: 0d,
            RotationRadians: 0d,
            StartOffsetMm: 0d,
            EndOffsetMm: 0d,
            reservedElementId);

    [Fact]
    public void DefinitionsEquivalent_IdenticalPayloads_ReturnsTrue()
    {
        Assert.True(RoofWholeRoofCopyIdentityRules.DefinitionsEquivalent(
            Definition(),
            Definition()));
    }

    [Fact]
    public void DefinitionsEquivalent_IdenticalPayloadsWithOverrides_ReturnsTrue()
    {
        var overrides = new[]
        {
            Override(suppressed: true),
            Override(suppressed: false, reservedElementId: "R-1"),
        };
        Assert.True(RoofWholeRoofCopyIdentityRules.DefinitionsEquivalent(
            Definition(overrides: overrides),
            Definition(overrides: overrides)));
    }

    [Fact]
    public void DefinitionsEquivalent_DifferentSlope_ReturnsFalse()
    {
        Assert.False(RoofWholeRoofCopyIdentityRules.DefinitionsEquivalent(
            Definition(slope: 35d),
            Definition(slope: 36d)));
    }

    [Fact]
    public void DefinitionsEquivalent_DifferentKind_ReturnsFalse()
    {
        Assert.False(RoofWholeRoofCopyIdentityRules.DefinitionsEquivalent(
            Definition(kind: RoofKind.SimpleGable),
            Definition(kind: RoofKind.AsymmetricGable, face1Slope: 30d)));
    }

    [Fact]
    public void DefinitionsEquivalent_DifferentSecondFaceSlope_ReturnsFalse()
    {
        Assert.False(RoofWholeRoofCopyIdentityRules.DefinitionsEquivalent(
            Definition(kind: RoofKind.AsymmetricGable, face1Slope: 30d),
            Definition(kind: RoofKind.AsymmetricGable, face1Slope: 31d)));
    }

    [Fact]
    public void DefinitionsEquivalent_DifferentEaveHeightDifference_ReturnsFalse()
    {
        Assert.False(RoofWholeRoofCopyIdentityRules.DefinitionsEquivalent(
            Definition(eaveHeightDifferenceMm: 0d),
            Definition(eaveHeightDifferenceMm: 250d)));
    }

    [Fact]
    public void DefinitionsEquivalent_DifferentEditState_ReturnsFalse()
    {
        Assert.False(RoofWholeRoofCopyIdentityRules.DefinitionsEquivalent(
            Definition(editState: RoofEditState.Locked),
            Definition(editState: RoofEditState.Unlocked)));
    }

    [Fact]
    public void DefinitionsEquivalent_DifferentOverrideSet_ReturnsFalse()
    {
        Assert.False(RoofWholeRoofCopyIdentityRules.DefinitionsEquivalent(
            Definition(overrides: new[] { Override(suppressed: true) }),
            Definition(overrides: Array.Empty<RoofGeneratedMemberOverride>())));
    }

    [Fact]
    public void DefinitionsEquivalent_DifferentSuppression_ReturnsFalse()
    {
        Assert.False(RoofWholeRoofCopyIdentityRules.DefinitionsEquivalent(
            Definition(overrides: new[] { Override(suppressed: false) }),
            Definition(overrides: new[] { Override(suppressed: true) })));
    }

    [Fact]
    public void DefinitionsEquivalent_DifferentReservedElementId_ReturnsFalse()
    {
        Assert.False(RoofWholeRoofCopyIdentityRules.DefinitionsEquivalent(
            Definition(overrides: new[] { Override(suppressed: false, reservedElementId: "R-1") }),
            Definition(overrides: new[] { Override(suppressed: false, reservedElementId: "R-2") })));
    }

    [Fact]
    public void DefinitionsEquivalent_NullHandling()
    {
        Assert.True(RoofWholeRoofCopyIdentityRules.DefinitionsEquivalent(null, null));
        Assert.False(RoofWholeRoofCopyIdentityRules.DefinitionsEquivalent(null, Definition()));
        Assert.False(RoofWholeRoofCopyIdentityRules.DefinitionsEquivalent(Definition(), null));
    }

    [Fact]
    public void IsCompleteAssemblyClone_FullCurrentAssembly_ReturnsTrue()
    {
        // The HOST source roof: generated=23 (one MIRROR-Yes suppressed member
        // physically absent), attachedManual=7. Completeness is the CURRENT physical
        // assembly, not the pristine canonical 24.
        Assert.True(RoofWholeRoofCopyIdentityRules.IsCompleteAssemblyClone(23, 7, 23, 7));
    }

    [Fact]
    public void IsCompleteAssemblyClone_PartialGenerated_ReturnsFalse()
    {
        Assert.False(RoofWholeRoofCopyIdentityRules.IsCompleteAssemblyClone(23, 7, 12, 7));
    }

    [Fact]
    public void IsCompleteAssemblyClone_PartialAttachedManual_ReturnsFalse()
    {
        Assert.False(RoofWholeRoofCopyIdentityRules.IsCompleteAssemblyClone(23, 7, 23, 3));
    }

    [Fact]
    public void IsCompleteAssemblyClone_ExtraClones_ReturnsFalse()
    {
        // Two copies of the same roof in one command double the appended set.
        Assert.False(RoofWholeRoofCopyIdentityRules.IsCompleteAssemblyClone(23, 7, 46, 14));
    }

    [Fact]
    public void IsCompleteAssemblyClone_ZeroTimberRoof_ReturnsFalse()
    {
        // Copying the source Polyline alone (or with display only) is not an assembly copy.
        Assert.False(RoofWholeRoofCopyIdentityRules.IsCompleteAssemblyClone(0, 0, 0, 0));
    }

    [Fact]
    public void IsCompleteAssemblyClone_ManualChildrenOnlyRoof_IsACompleteAssembly()
    {
        // A roof whose entire generated set is suppressed still owns AttachedManual
        // children: copying them all together with the source is a whole-roof copy.
        Assert.True(RoofWholeRoofCopyIdentityRules.IsCompleteAssemblyClone(0, 3, 0, 3));
    }

    [Fact]
    public void IsCompleteAssemblyClone_NegativeCounts_ReturnsFalse()
    {
        Assert.False(RoofWholeRoofCopyIdentityRules.IsCompleteAssemblyClone(-1, 7, 23, 7));
        Assert.False(RoofWholeRoofCopyIdentityRules.IsCompleteAssemblyClone(23, -1, 23, 7));
    }

    [Fact]
    public void ClassifyPairing_UniqueAmbiguousNone()
    {
        Assert.Equal(
            RoofWholeRoofCopyIdentityRules.RoofWholeRoofCopyPairing.Unique,
            RoofWholeRoofCopyIdentityRules.ClassifyPairing(1));
        Assert.Equal(
            RoofWholeRoofCopyIdentityRules.RoofWholeRoofCopyPairing.Ambiguous,
            RoofWholeRoofCopyIdentityRules.ClassifyPairing(2));
        Assert.Equal(
            RoofWholeRoofCopyIdentityRules.RoofWholeRoofCopyPairing.None,
            RoofWholeRoofCopyIdentityRules.ClassifyPairing(0));
    }
}
