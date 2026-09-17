using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// CAD-neutral desired-set parity for the authoritative rebuild used after a
/// same-DWG whole-roof COPY. Host callback timing remains a separate HOST proof.
/// </summary>
public sealed class RoofWholeRoofCopyHipParityTests
{
    public static IEnumerable<object[]> HipFixtures()
    {
        yield return ["rectangle", Rectangle(), 32, 4, 0];
        yield return ["complex X", ComplexX(), 46, 8, 4];
    }

    [Theory]
    [MemberData(nameof(HipFixtures))]
    public void TranslatedCopy_RebuildsEquivalentUniqueOrdinaryAndStructuralDesiredSets(
        string name,
        RoofPoint2D[] footprint,
        int expectedOrdinary,
        int expectedHip,
        int expectedValley)
    {
        var source = CreateDesiredSet(footprint);
        var copy = CreateDesiredSet(Translate(footprint, 20000d, -15000d));

        Assert.Equal(expectedOrdinary, source.Ordinary.Rafters.Count);
        Assert.Equal(expectedOrdinary, copy.Ordinary.Rafters.Count);
        Assert.Equal(expectedHip, Count(source.Structural, TimberElementType.HipRafter));
        Assert.Equal(expectedHip, Count(copy.Structural, TimberElementType.HipRafter));
        Assert.Equal(expectedValley, Count(source.Structural, TimberElementType.ValleyRafter));
        Assert.Equal(expectedValley, Count(copy.Structural, TimberElementType.ValleyRafter));
        Assert.Equal(
            source.Ordinary.Rafters.Select(item => item.LogicalKey),
            copy.Ordinary.Rafters.Select(item => item.LogicalKey));
        Assert.Equal(
            source.Structural.Items.Select(item => item.LogicalKey),
            copy.Structural.Items.Select(item => item.LogicalKey));
        Assert.Equal(
            expectedOrdinary,
            copy.Ordinary.Rafters.Select(item => item.LogicalKey).Distinct().Count());
        Assert.Equal(
            expectedHip + expectedValley,
            copy.Structural.Items.Select(item => item.LogicalKey).Distinct().Count());
        Assert.NotEqual(source.Geometry.Signature, copy.Geometry.Signature);
        Assert.All(copy.Structural.Items, AssertPhysicalStructuralMetadata);

        _ = name;
    }

    [Fact]
    public void ResizingCopiedComplexX_RebuildsOnlyTheCopiedDesiredState()
    {
        var sourceFootprint = ComplexX();
        var sourceBefore = CreateDesiredSet(sourceFootprint);
        var copiedFootprint = Translate(sourceFootprint, 20000d, -15000d);
        var copyBefore = CreateDesiredSet(copiedFootprint);
        var resizedCopy = copiedFootprint
            .Select((point, index) => index is 5 or 6
                ? new RoofPoint2D(point.X + 1000d, point.Y)
                : point)
            .ToArray();
        var copyAfter = CreateDesiredSet(resizedCopy);
        var sourceAfter = CreateDesiredSet(sourceFootprint);

        Assert.Equal(sourceBefore.Geometry.Signature, sourceAfter.Geometry.Signature);
        Assert.Equal(
            sourceBefore.Ordinary.Rafters.Select(DescribeOrdinary),
            sourceAfter.Ordinary.Rafters.Select(DescribeOrdinary));
        Assert.Equal(
            sourceBefore.Structural.Items.Select(DescribeStructural),
            sourceAfter.Structural.Items.Select(DescribeStructural));
        Assert.NotEqual(copyBefore.Geometry.Signature, copyAfter.Geometry.Signature);
        Assert.True(copyAfter.Ordinary.Rafters.Count > 0);
        Assert.Equal(8, Count(copyAfter.Structural, TimberElementType.HipRafter));
        Assert.Equal(4, Count(copyAfter.Structural, TimberElementType.ValleyRafter));
        Assert.Equal(
            copyAfter.Structural.Items.Count,
            copyAfter.Structural.Items.Select(item => item.LogicalKey).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(HipFixtures))]
    public void MirroredFootprint_RebuildsEquivalentUniqueOrdinaryAndStructuralDesiredSets(
        string name,
        RoofPoint2D[] footprint,
        int expectedOrdinary,
        int expectedHip,
        int expectedValley)
    {
        var source = CreateDesiredSet(footprint);
        // Rigid XY mirror reverses winding; authoritative rebuild must still produce
        // Hip/Valley parity and physically downhill annotation semantics.
        var mirrored = CreateDesiredSet(MirrorAcrossX(footprint));

        Assert.Equal(expectedOrdinary, source.Ordinary.Rafters.Count);
        Assert.Equal(expectedOrdinary, mirrored.Ordinary.Rafters.Count);
        Assert.Equal(expectedHip, Count(source.Structural, TimberElementType.HipRafter));
        Assert.Equal(expectedHip, Count(mirrored.Structural, TimberElementType.HipRafter));
        Assert.Equal(expectedValley, Count(source.Structural, TimberElementType.ValleyRafter));
        Assert.Equal(expectedValley, Count(mirrored.Structural, TimberElementType.ValleyRafter));
        Assert.Equal(
            expectedOrdinary,
            mirrored.Ordinary.Rafters.Select(item => item.LogicalKey).Distinct().Count());
        Assert.Equal(
            expectedHip + expectedValley,
            mirrored.Structural.Items.Select(item => item.LogicalKey).Distinct().Count());
        Assert.All(mirrored.Structural.Items, AssertPhysicalStructuralMetadata);
        Assert.NotEqual(source.Geometry.Signature, mirrored.Geometry.Signature);

        _ = name;
    }

    [Fact]
    public void MirroredComplexX_KeepsSourceDesiredSetUnchanged()
    {
        var sourceFootprint = ComplexX();
        var sourceBefore = CreateDesiredSet(sourceFootprint);
        var mirrored = CreateDesiredSet(MirrorAcrossX(sourceFootprint));
        var sourceAfter = CreateDesiredSet(sourceFootprint);

        Assert.Equal(sourceBefore.Geometry.Signature, sourceAfter.Geometry.Signature);
        Assert.Equal(
            sourceBefore.Ordinary.Rafters.Select(DescribeOrdinary),
            sourceAfter.Ordinary.Rafters.Select(DescribeOrdinary));
        Assert.Equal(
            sourceBefore.Structural.Items.Select(DescribeStructural),
            sourceAfter.Structural.Items.Select(DescribeStructural));
        Assert.Equal(8, Count(mirrored.Structural, TimberElementType.HipRafter));
        Assert.Equal(4, Count(mirrored.Structural, TimberElementType.ValleyRafter));
        Assert.All(mirrored.Structural.Items, AssertPhysicalStructuralMetadata);
    }

    [Fact]
    public void LiveResizeUpdateGeometryOnMirroredHip_StillPairsViaOrientationFlipTolerance()
    {
        // HOST path: LiveResize may rewrite the mirrored owner's RigidFootprint with
        // flipped SourceOrientation before pairing. Whole-roof detection must still
        // match. Separately, rebind-before-LiveResize keeps verbatim XData; CollectOwners
        // Classify fallback covers that mirrored polyline when strict RestoreHip fails.
        var sourceFootprint = Rectangle();
        var sourceInput = new RoofFootprintInput(sourceFootprint, IsClosed: true);
        var sourceNormalized = RoofFootprintValidator.ValidateWithProvenance(sourceInput);
        Assert.True(sourceNormalized.Validation.IsValid);
        var sourceSolved = RoofGeometrySolver.Solve(new RoofDefinition(
            sourceNormalized.Validation.Footprint!,
            new RoofParameters(30d),
            RoofKind.Hip));
        Assert.True(sourceSolved.IsValid);
        var sourceDefinition = RoofDefinitionPersistence.Create(
            sourceInput,
            sourceNormalized.Validation.Footprint!,
            sourceSolved.Geometry!);

        var mirroredFootprint = MirrorAcrossX(sourceFootprint);
        var mirroredInput = new RoofFootprintInput(mirroredFootprint, IsClosed: true);
        var mirroredNormalized = RoofFootprintValidator.ValidateWithProvenance(mirroredInput);
        Assert.True(mirroredNormalized.Validation.IsValid);
        var mirroredSolved = RoofGeometrySolver.Solve(new RoofDefinition(
            mirroredNormalized.Validation.Footprint!,
            new RoofParameters(30d),
            RoofKind.Hip));
        Assert.True(mirroredSolved.IsValid);

        Assert.True(RoofWholeRoofCopyIdentityRules.DefinitionsEquivalent(
            sourceDefinition,
            sourceDefinition with { }));

        var rewritten = RoofDefinitionPersistence.UpdateGeometry(
            sourceDefinition,
            mirroredInput,
            mirroredSolved.Geometry!);
        Assert.NotEqual(sourceDefinition.RigidFootprint, rewritten.RigidFootprint);
        Assert.True(RoofWholeRoofCopyIdentityRules.RigidFootprintsEquivalent(
            sourceDefinition.RigidFootprint,
            rewritten.RigidFootprint));
        Assert.True(RoofWholeRoofCopyIdentityRules.DefinitionsEquivalent(
            sourceDefinition,
            rewritten));

        // Strict Restore rejects mirrored polyline + verbatim (pre-flip) definition;
        // Classify accepts the orientation flip so CollectOwners can see the new owner.
        var restore = RoofDefinitionPersistence.Restore(
            mirroredInput,
            mirroredNormalized.Validation.Footprint!,
            sourceDefinition);
        Assert.False(restore.IsValid);
        var classify = RoofDefinitionPersistence.Classify(
            mirroredInput,
            mirroredNormalized.Validation.Footprint!,
            sourceDefinition);
        Assert.NotNull(classify.Geometry);
        Assert.True(
            classify.Kind is RoofSourceChangeKind.RigidEquivalent or
                RoofSourceChangeKind.SupportedResize);
    }

    private static DesiredSet CreateDesiredSet(RoofPoint2D[] footprint)
    {
        var input = new RoofFootprintInput(footprint, IsClosed: true);
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid, normalized.Validation.Error.ToString());
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            normalized.Validation.Footprint!,
            new RoofParameters(30d),
            RoofKind.Hip));
        Assert.True(solved.IsValid, solved.Error.ToString());
        var geometry = Assert.IsType<HipRoofGeometry>(solved.Geometry);
        var ordinaryResult = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(900d, 80d, 500d));
        Assert.True(ordinaryResult.IsValid, ordinaryResult.Error.ToString());

        var identity = RoofBoundaryIdentityRules.CreateSequential(
            normalized.EdgeProvenance.Count,
            normalized.Validation.SourceOrientation).Identity!;
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity);
        var resolution = RoofStructuralEdgeIdentityResolver.Resolve(geometry, provenance);
        Assert.True(resolution.IsValid, resolution.Error.ToString());
        var structural = RoofAutomaticStructuralRafterPlanner.Create(resolution);
        Assert.True(structural.IsValid, structural.Error.ToString());
        return new DesiredSet(geometry, ordinaryResult.Layout!, structural);
    }

    private static void AssertPhysicalStructuralMetadata(
        RoofAutomaticStructuralRafterPlanItem item)
    {
        Assert.Equal(LengthCalculationMode.PlanLength, item.TimberData.LengthCalculationMode);
        Assert.Equal(item.True3DLengthMm, item.Segment3D.LengthMm, 8);
        Assert.Equal(
            item.Segment3D.InclinationDegreesAboveHorizontal,
            item.TimberData.SlopeDegrees,
            8);
        Assert.Equal(
            TimberSlopeDirectionRules.ResolveIsReversedForDownhillDisplay(item.Segment3D),
            item.TimberData.IsSlopeDirectionReversed);
    }

    private static int Count(
        RoofAutomaticStructuralRafterPlanResult plan,
        TimberElementType type) =>
        plan.Items.Count(item => item.ElementType == type);

    private static string DescribeOrdinary(RoofRafterGeometry item) =>
        string.Join("|", item.LogicalKey, item.PlanStart, item.PlanEnd, item.TrueLengthMm);

    private static string DescribeStructural(RoofAutomaticStructuralRafterPlanItem item) =>
        string.Join("|", item.LogicalKey, item.Segment3D.Start, item.Segment3D.End);

    private static RoofPoint2D[] Translate(
        IEnumerable<RoofPoint2D> points,
        double x,
        double y) =>
        points.Select(point => new RoofPoint2D(point.X + x, point.Y + y)).ToArray();

    private static RoofPoint2D[] MirrorAcrossX(IEnumerable<RoofPoint2D> points) =>
        points.Select(point => new RoofPoint2D(point.X, -point.Y)).ToArray();

    private static RoofPoint2D[] Rectangle() =>
        [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)];

    // Four reflex corners exercise the X topology seen in HOST: 8 Hip + 4 Valley.
    // At the current 900/80/500 ordinary-rafter contract it produces 46 members.
    private static RoofPoint2D[] ComplexX() =>
    [
        new(0, 2000), new(2000, 2000), new(2000, 0), new(6000, 0),
        new(6000, 2000), new(8000, 2000), new(8000, 5500), new(6000, 5500),
        new(6000, 8000), new(2000, 8000), new(2000, 5500), new(0, 5500),
    ];

    private sealed record DesiredSet(
        HipRoofGeometry Geometry,
        RoofRafterLayout Ordinary,
        RoofAutomaticStructuralRafterPlanResult Structural);
}
