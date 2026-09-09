using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedMemberLockedTamperRulesTests
{
    [Theory]
    [InlineData("MOVE")]
    [InlineData("ROTATE")]
    [InlineData("SCALE")]
    [InlineData("STRETCH")]
    [InlineData("GRIP_STRETCH")]
    public void LockedGeneratedChildEdit_ClassifiesAsLockedGeneratedTamper(string command)
    {
        var classification = RoofGeneratedMemberLockedTamperRules.Classify(
            RoofEditState.Locked,
            command,
            sourceModified: false,
            generatedMemberModified: true,
            ownedAnnotationModified: false,
            RoofSourceChangeKind.RigidEquivalent);

        Assert.Equal(
            RoofGeneratedMemberLockedTamperRules.Classification.LockedGeneratedTamper,
            classification);
        Assert.True(RoofGeneratedMemberLockedTamperRules.ShouldQueueLockedGeneratedRecovery(
            RoofEditState.Locked,
            command,
            sourceModified: false,
            generatedOrAnnotationModified: true,
            RoofSourceChangeKind.RigidEquivalent));
    }

    [Fact]
    public void LockedAnnotationOnlyEdit_ClassifiesAsLockedAnnotationTamper()
    {
        Assert.Equal(
            RoofGeneratedMemberLockedTamperRules.Classification.LockedAnnotationTamper,
            RoofGeneratedMemberLockedTamperRules.Classify(
                RoofEditState.Locked,
                "MOVE",
                sourceModified: false,
                generatedMemberModified: false,
                ownedAnnotationModified: true,
                RoofSourceChangeKind.RigidEquivalent));
        Assert.True(RoofGeneratedMemberLockedTamperRules.ShouldQueueLockedGeneratedRecovery(
            RoofEditState.Locked,
            "MOVE",
            sourceModified: false,
            generatedOrAnnotationModified: true,
            RoofSourceChangeKind.SupportedResize));
        Assert.False(RoofGeneratedMemberLockedTamperRules.ShouldDeferUnlockedAnnotationPresentationOnly(
            RoofEditState.Locked,
            generatedMemberModified: false,
            ownedAnnotationModified: true));
    }

    [Theory]
    [InlineData("DBText")]
    [InlineData("MLeader")]
    [InlineData("Polyline")]
    public void LockedAnnotationEntityKinds_ShareLockedAnnotationTamperClassification(string _)
    {
        // Owner resolution is SourceHandle-based for DBText/MLeader/frame Polyline;
        // classification is entity-agnostic once ownedAnnotationModified is proven.
        Assert.Equal(
            RoofGeneratedMemberLockedTamperRules.Classification.LockedAnnotationTamper,
            RoofGeneratedMemberLockedTamperRules.Classify(
                RoofEditState.Locked,
                "MOVE",
                sourceModified: false,
                generatedMemberModified: false,
                ownedAnnotationModified: true,
                RoofSourceChangeKind.SupportedResize));
    }

    [Fact]
    public void UnlockedAnnotationOnly_DefersToPresentationPath()
    {
        Assert.True(RoofGeneratedMemberLockedTamperRules.ShouldDeferUnlockedAnnotationPresentationOnly(
            RoofEditState.Unlocked,
            generatedMemberModified: false,
            ownedAnnotationModified: true));
        Assert.False(RoofGeneratedMemberLockedTamperRules.ShouldDeferUnlockedAnnotationPresentationOnly(
            RoofEditState.Unlocked,
            generatedMemberModified: true,
            ownedAnnotationModified: true));
        Assert.Equal(
            RoofGeneratedMemberLockedTamperRules.Classification.NotApplicable,
            RoofGeneratedMemberLockedTamperRules.Classify(
                RoofEditState.Unlocked,
                "MOVE",
                sourceModified: false,
                generatedMemberModified: false,
                ownedAnnotationModified: true,
                RoofSourceChangeKind.RigidEquivalent));
    }

    [Fact]
    public void RelockedAnnotationProtection_QueuesAgain()
    {
        Assert.True(RoofGeneratedMemberLockedTamperRules.ShouldQueueLockedGeneratedRecovery(
            RoofEditState.Locked,
            "MOVE",
            sourceModified: false,
            generatedOrAnnotationModified: true,
            RoofSourceChangeKind.RigidEquivalent));
    }

    [Fact]
    public void AuthoritativeSourceStretch_ClassifiesAsSupportedSourceResize()
    {
        Assert.Equal(
            RoofGeneratedMemberLockedTamperRules.Classification.SupportedSourceResize,
            RoofGeneratedMemberLockedTamperRules.Classify(
                RoofEditState.Locked,
                "STRETCH",
                sourceModified: true,
                generatedMemberModified: true,
                ownedAnnotationModified: true,
                RoofSourceChangeKind.SupportedResize));
        Assert.False(RoofGeneratedMemberLockedTamperRules.ShouldQueueLockedGeneratedRecovery(
            RoofEditState.Locked,
            "STRETCH",
            sourceModified: true,
            generatedOrAnnotationModified: true,
            RoofSourceChangeKind.SupportedResize));
    }

    [Fact]
    public void UndoRedo_IsIgnored()
    {
        Assert.Equal(
            RoofGeneratedMemberLockedTamperRules.Classification.IgnoreUndoRedo,
            RoofGeneratedMemberLockedTamperRules.Classify(
                RoofEditState.Locked,
                "U",
                sourceModified: false,
                generatedMemberModified: true,
                ownedAnnotationModified: false,
                RoofSourceChangeKind.RigidEquivalent));
    }

    [Fact]
    public void UnlockedGeneratedEdit_IsNotLockedTamper()
    {
        Assert.Equal(
            RoofGeneratedMemberLockedTamperRules.Classification.NotApplicable,
            RoofGeneratedMemberLockedTamperRules.Classify(
                RoofEditState.Unlocked,
                "MOVE",
                sourceModified: false,
                generatedMemberModified: true,
                ownedAnnotationModified: false,
                RoofSourceChangeKind.RigidEquivalent));
    }

    [Fact]
    public void HipCompactDescriptorCollision_RemainsRigidEquivalentForLockedRecovery()
    {
        // Non-rectangular Hip with unchanged Edge01/Edge12 must stay RigidEquivalent in
        // Core Classify so Locked generated recovery / assembly snapshots remain eligible.
        // Host promotes SupportedResize only when display/coverage proves a real reshape.
        var originalInput = Input(LShape(8000d, 8000d));
        var original = Validate(originalInput);
        var stored = RoofDefinitionPersistence.Create(
            originalInput,
            original,
            Solve(original, 30d));
        var changedInput = Input(LShape(8000d, 9000d));
        var changed = Validate(changedInput);

        var classification = RoofDefinitionPersistence.Classify(changedInput, changed, stored);
        Assert.Equal(RoofSourceChangeKind.RigidEquivalent, classification.Kind);
        Assert.True(RoofGeneratedMemberLockedTamperRules.ShouldQueueLockedGeneratedRecovery(
            RoofEditState.Locked,
            "MOVE",
            sourceModified: false,
            generatedOrAnnotationModified: true,
            classification.Kind));
    }

    private static RoofPoint2D[] LShape(double width, double upperHeight) =>
        [new(0, 0), new(width, 0), new(width, 3000), new(3000, 3000), new(3000, upperHeight), new(0, upperHeight)];

    private static RoofFootprintInput Input(IReadOnlyList<RoofPoint2D> points) =>
        new(points, true, false, true);

    private static RoofFootprint Validate(RoofFootprintInput input)
    {
        var validation = RoofFootprintValidator.Validate(input);
        Assert.True(validation.IsValid, validation.Error.ToString());
        return validation.Footprint!;
    }

    private static HipRoofGeometry Solve(RoofFootprint footprint, double slope)
    {
        var result = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint,
            new RoofParameters(slope),
            RoofKind.Hip));
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<HipRoofGeometry>(result.Geometry);
    }
}
