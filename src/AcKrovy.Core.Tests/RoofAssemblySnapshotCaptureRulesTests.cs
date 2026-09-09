using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAssemblySnapshotCaptureRulesTests
{
    public static IEnumerable<object[]> HipShapes()
    {
        yield return ["Rectangle", Rectangle()];
        yield return ["L", LShape()];
        yield return ["U", UShape()];
        yield return ["T", TShape()];
        yield return ["Offset", OffsetParallel()];
    }

    [Theory]
    [MemberData(nameof(HipShapes))]
    public void ClosingVertexDuplication_DoesNotChangeHipClassification(string name, RoofPoint2D[] polygon)
    {
        var withoutClose = new RoofFootprintInput(polygon, true);
        var withClose = new RoofFootprintInput(polygon.Concat([polygon[0]]).ToArray(), true);
        var footprint = Validate(withoutClose);
        var stored = RoofDefinitionPersistence.Create(
            withoutClose,
            footprint,
            Solve(footprint, 30d));

        Assert.True(
            RoofAssemblySnapshotCaptureRules.ClosingVertexDuplicationIsSemanticallyEquivalent(
                withClose,
                withoutClose,
                stored),
            name);

        var sevenValidation = RoofFootprintValidator.Validate(withClose);
        Assert.True(sevenValidation.IsValid, name);
        Assert.Equal(polygon.Length, sevenValidation.Footprint!.Vertices.Count);

        var classifySix = RoofDefinitionPersistence.Classify(withoutClose, footprint, stored);
        var classifySeven = RoofDefinitionPersistence.Classify(
            withClose,
            sevenValidation.Footprint,
            stored);
        Assert.Equal(RoofSourceChangeKind.RigidEquivalent, classifySix.Kind);
        Assert.Equal(classifySix.Kind, classifySeven.Kind);
        Assert.True(RoofAssemblySnapshotCaptureRules.CanCaptureSource(
            true,
            classifySeven.Kind,
            classifySeven.Geometry is not null));
    }

    [Fact]
    public void SupportedResize_StillCapturableWhenGeometrySolves()
    {
        Assert.True(RoofAssemblySnapshotCaptureRules.CanCaptureSource(
            footprintValid: true,
            RoofSourceChangeKind.SupportedResize,
            classificationHasGeometry: true));
        Assert.False(RoofAssemblySnapshotCaptureRules.CanCaptureSource(
            footprintValid: true,
            RoofSourceChangeKind.SupportedResize,
            classificationHasGeometry: false));
        Assert.False(RoofAssemblySnapshotCaptureRules.CanCaptureSource(
            footprintValid: true,
            RoofSourceChangeKind.Unsupported,
            classificationHasGeometry: false));
        Assert.True(RoofAssemblySnapshotCaptureRules.CanCaptureSource(
            footprintValid: true,
            RoofSourceChangeKind.RigidEquivalent,
            classificationHasGeometry: true));
    }

    [Fact]
    public void LockedGeneratedTamper_StillQueuesWhenClassifierIsSupportedResize()
    {
        // Child-only MOVE on a Hip whose compact descriptor is stale must still recover
        // when a pre-command snapshot exists.
        Assert.True(RoofGeneratedMemberLockedTamperRules.ShouldQueueLockedGeneratedRecovery(
            RoofEditState.Locked,
            "MOVE",
            sourceModified: false,
            generatedOrAnnotationModified: true,
            RoofSourceChangeKind.SupportedResize));
    }

    [Fact]
    public void SourceModifiedSupportedResize_OwnsOverChildTamper()
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
    }

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

    private static RoofPoint2D[] Rectangle() =>
        [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)];

    private static RoofPoint2D[] LShape() =>
    [
        new(0, 0), new(8000, 0), new(8000, 3000),
        new(3000, 3000), new(3000, 8000), new(0, 8000),
    ];

    private static RoofPoint2D[] UShape() =>
    [
        new(0, 0), new(10000, 0), new(10000, 9000), new(7000, 9000),
        new(7000, 3000), new(3000, 3000), new(3000, 9000), new(0, 9000),
    ];

    private static RoofPoint2D[] TShape() =>
    [
        new(0, 0), new(10000, 0), new(10000, 3000), new(6500, 3000),
        new(6500, 9000), new(3500, 9000), new(3500, 3000), new(0, 3000),
    ];

    private static RoofPoint2D[] OffsetParallel() =>
    [
        new(0, 0), new(16000, 0), new(16000, 5000), new(10000, 5000),
        new(10000, 12000), new(5000, 12000), new(5000, 3000), new(0, 3000),
    ];
}
