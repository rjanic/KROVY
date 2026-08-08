using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberFramedBlockContentGripPresentationRulesTests
{
    public static IEnumerable<object[]> RequiredHostMatrixAngles()
    {
        foreach (var deg in new[] { 0d, 35d, -35d, 90d, 135d, 225d, 270d, 315d })
        {
            yield return [deg];
        }
    }

    public static IEnumerable<object[]> RadialAngles()
    {
        foreach (var deg in TimberFramedBlockContentGripPresentationRules
                     .RadialSourceAnglesDegrees)
        {
            yield return [deg];
        }
    }

    [Theory]
    [MemberData(nameof(RadialAngles))]
    public void KneeGrip_UnchangedSource_MustSyncPresentationToFinalLanding(
        double sourceDeg)
    {
        var source = sourceDeg * Math.PI / 180d;
        Assert.True(
            TimberFramedBlockContentGripPresentationRules
                .MustSyncPresentationToFinalLandingAfterKneeGrip(source, source));

        // Landing aligned with CREATE readable axis → same presentation.
        var createPresentation =
            TimberFramedBlockContentGripPresentationRules
                .ExpectedCreatePresentationRadians(source);
        Assert.True(
            TimberFramedBlockContentGripPresentationRules
                .TryResolvePresentationFromFinalLandingRadians(
                    kneeX: 0d,
                    kneeY: 0d,
                    frameX: Math.Cos(source) * 100d,
                    frameY: Math.Sin(source) * 100d,
                    out var fromLanding));
        Assert.True(
            TimberFramedBlockContentGripPresentationRules.PresentationPreserved(
                createPresentation,
                fromLanding));
    }

    [Theory]
    [InlineData(0d, 35d)]
    [InlineData(0d, 90d)]
    [InlineData(35d, -35d)]
    [InlineData(90d, 0d)]
    [InlineData(135d, 225d)]
    public void KneeGrip_LandingRotates_PresentationFollowsFinalLanding(
        double createDeg,
        double landingDeg)
    {
        var create = createDeg * Math.PI / 180d;
        var landing = landingDeg * Math.PI / 180d;
        var createPresentation =
            TimberFramedBlockContentGripPresentationRules
                .ExpectedCreatePresentationRadians(create);
        var expected =
            TimberFramedBlockContentReadableOrientationRules
                .Decide(landing)
                .PresentationAngle;

        Assert.True(
            TimberFramedBlockContentGripPresentationRules
                .TryResolvePresentationFromFinalLandingRadians(
                    0d,
                    0d,
                    Math.Cos(landing) * 80d,
                    Math.Sin(landing) * 80d,
                    out var after));

        Assert.True(
            TimberFramedBlockContentGripPresentationRules.PresentationPreserved(
                expected,
                after));
        Assert.True(
            TimberFramedBlockContentGripPresentationRules
                .PresentationFollowsFinalLanding(
                    after,
                    0d,
                    0d,
                    Math.Cos(landing) * 80d,
                    Math.Sin(landing) * 80d));

        // When landing rotates away from CREATE axis, presentation may change.
        var landingDiffersFromCreate =
            Math.Abs(
                TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                    landing - create)) >
            1e-9d;
        if (landingDiffersFromCreate &&
            Math.Abs(
                TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                    expected - createPresentation)) >
            1e-9d)
        {
            Assert.False(
                TimberFramedBlockContentGripPresentationRules.PresentationPreserved(
                    createPresentation,
                    after),
                "Rotated landing must not lock pre-grip CREATE presentation.");
        }
    }

    [Theory]
    [MemberData(nameof(RadialAngles))]
    public void SideCrossing_MayChangeVariant_PresentationStillFollowsLanding(
        double landingDeg)
    {
        var landing = landingDeg * Math.PI / 180d;
        var expected =
            TimberFramedBlockContentReadableOrientationRules
                .Decide(landing)
                .PresentationAngle;

        // BTR RIGHT→LEFT may change semantic layout (B); layer C still Decide(landing).
        Assert.True(
            TimberFramedBlockContentGripPresentationRules
                .TryResolvePresentationFromFinalLandingRadians(
                    10d,
                    20d,
                    10d + (Math.Cos(landing) * 50d),
                    20d + (Math.Sin(landing) * 50d),
                    out var afterSwap));
        Assert.True(
            TimberFramedBlockContentGripPresentationRules.PresentationPreserved(
                expected,
                afterSwap));
    }

    [Fact]
    public void SourceAxisChanged_DoesNotRequireKneeGripLandingSync()
    {
        Assert.False(
            TimberFramedBlockContentGripPresentationRules
                .MustSyncPresentationToFinalLandingAfterKneeGrip(
                    0d,
                    Math.PI / 2d));
    }

    [Fact]
    public void DegenerateLanding_FailsClosed()
    {
        Assert.False(
            TimberFramedBlockContentGripPresentationRules
                .TryResolveLandingPhysicalAngleRadians(
                    1d,
                    1d,
                    1d,
                    1d,
                    out _));
        Assert.False(
            TimberFramedBlockContentGripPresentationRules
                .TryResolvePresentationFromFinalLandingRadians(
                    1d,
                    1d,
                    1d,
                    1d,
                    out _));
    }

    [Fact]
    public void ResolvePreservedPresentation_StillSupportsSourceStretchCapture()
    {
        // Source-stretch / AttrRef capture path must keep working independently.
        var preGripPresentation = -Math.PI / 2d;
        var restored =
            TimberFramedBlockContentGripPresentationRules
                .ResolvePreservedPresentationRadians(
                    0d,
                    preGripPresentation);
        Assert.Equal(preGripPresentation, restored, 12);
    }

    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(35d, 35d)]
    [InlineData(90d, -90d)]
    [InlineData(135d, -45d)]
    [InlineData(180d, 0d)]
    [InlineData(225d, 45d)]
    [InlineData(270d, -90d)]
    [InlineData(315d, -45d)]
    public void RadialCreatePresentationTable_MatchesWhiteContract(
        double sourceDeg,
        double expectedPresentationDeg)
    {
        var presentation =
            TimberFramedBlockContentGripPresentationRules
                .ExpectedCreatePresentationRadians(sourceDeg * Math.PI / 180d) *
            180d /
            Math.PI;
        Assert.Equal(expectedPresentationDeg, presentation, 10);
    }

    [Theory]
    [MemberData(nameof(RequiredHostMatrixAngles))]
    public void CreateTransformBy_ThenNativeLikeKneeMove_UsesRelativeBlockRotation(
        double sourceDeg)
    {
        var source = sourceDeg * Math.PI / 180d;
        var createWorld =
            TimberFramedBlockContentReadableOrientationRules
                .Decide(source)
                .PresentationAngle;

        // CREATE host state: TransformBy owns the world basis while BR stays 0.
        const double createBlockRotation = 0d;
        var finalLanding = source + (20d * Math.PI / 180d);
        var resolved =
            TimberFramedBlockContentGripPresentationRules
                .ResolveFinalContentPresentation(
                    createWorld,
                    createBlockRotation,
                    finalLanding);
        var simulatedFinalWorld =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                createWorld + resolved.BlockRotationCorrection);

        AssertAngleEqual(resolved.FinalWorldPresentationAngle, simulatedFinalWorld);
        AssertAngleEqual(
            TimberFramedBlockContentReadableOrientationRules
                .Decide(finalLanding)
                .PresentationAngle,
            simulatedFinalWorld);
        Assert.True(
            TimberFramedBlockContentReadableOrientationRules.IsReadableTextAngleDegrees(
                simulatedFinalWorld * 180d / Math.PI));
    }

    [Theory]
    [MemberData(nameof(RequiredHostMatrixAngles))]
    public void CreateTransformBy_ThenSideCrossing_PreservesTowardKneeOrder(
        double sourceDeg)
    {
        var source = sourceDeg * Math.PI / 180d;
        var createWorld =
            TimberFramedBlockContentReadableOrientationRules
                .Decide(source)
                .PresentationAngle;
        var crossedLanding = source + (160d * Math.PI / 180d);
        var resolved =
            TimberFramedBlockContentGripPresentationRules
                .ResolveFinalContentPresentation(
                    createWorld,
                    currentBlockRotationRadians: 0d,
                    finalLandingPhysicalAngleRadians: crossedLanding);

        const double landingLength = 100d;
        const double dimensionOffset = 20d;
        var frameX = Math.Cos(crossedLanding) * landingLength;
        var frameY = Math.Sin(crossedLanding) * landingLength;
        var axisX = Math.Cos(resolved.FinalWorldPresentationAngle);
        var axisY = Math.Sin(resolved.FinalWorldPresentationAngle);
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.TryResolveRequiredContentVariant(
                kneeX: 0d,
                kneeY: 0d,
                frameCenterX: frameX,
                frameCenterY: frameY,
                effectiveLocalXAxisX: axisX,
                effectiveLocalXAxisY: axisY,
                out var side,
                out _,
                out _));

        var signedOffset =
            side == AcKrovy.Core.Models.TimberFramedBlockContentDimensionColumnSide
                .NegativeLocalX
                ? -dimensionOffset
                : dimensionOffset;
        var dimensionsX = frameX + (axisX * signedOffset);
        var dimensionsY = frameY + (axisY * signedOffset);
        var towardKneeDot =
            ((dimensionsX - frameX) * -frameX) +
            ((dimensionsY - frameY) * -frameY);

        Assert.True(towardKneeDot > 0d);
        AssertAngleEqual(
            TimberFramedBlockContentReadableOrientationRules
                .Decide(crossedLanding)
                .PresentationAngle,
            resolved.FinalWorldPresentationAngle);
    }

    [Fact]
    public void AbsoluteWorldAngleWouldDoubleRotate_ResolvedTargetDoesNot()
    {
        var angle = 35d * Math.PI / 180d;
        var resolved =
            TimberFramedBlockContentGripPresentationRules
                .ResolveFinalContentPresentation(
                    currentWorldPresentationRadians: angle,
                    currentBlockRotationRadians: 0d,
                    finalLandingPhysicalAngleRadians: angle);

        AssertAngleEqual(0d, resolved.TargetBlockRotation);
        AssertAngleEqual(angle, resolved.FinalWorldPresentationAngle);
        Assert.False(
            TimberFramedBlockContentGripPresentationRules.PresentationPreserved(
                angle,
                angle + angle));
    }

    [Fact]
    public void RefreshStateWithoutTransformBy_StillUsesAbsoluteBlockRotation()
    {
        var current = -35d * Math.PI / 180d;
        var landing = 80d * Math.PI / 180d;
        var resolved =
            TimberFramedBlockContentGripPresentationRules
                .ResolveFinalContentPresentation(
                    currentWorldPresentationRadians: current,
                    currentBlockRotationRadians: current,
                    finalLandingPhysicalAngleRadians: landing);

        AssertAngleEqual(0d, resolved.ExistingContentBaseAngle);
        AssertAngleEqual(
            TimberFramedBlockContentReadableOrientationRules
                .Decide(landing)
                .PresentationAngle,
            resolved.TargetBlockRotation);
    }

    [Fact]
    public void AdoptedNegativeSourceReference_ExactPositiveVerticalGripKeepsFamily()
    {
        var resolved = TimberFramedBlockContentGripPresentationRules
            .ResolveFinalContentPresentation(
                currentWorldPresentationRadians: Math.PI / 2d,
                currentBlockRotationRadians: Math.PI,
                finalLandingPhysicalAngleRadians: Math.PI / 2d,
                preserveAdoptedReferenceVerticalFamily: true);

        AssertAngleEqual(Math.PI / 2d, resolved.FinalWorldPresentationAngle);
        AssertAngleEqual(Math.PI, resolved.TargetBlockRotation);
        AssertAngleEqual(0d, resolved.BlockRotationCorrection);
    }

    [Theory]
    [InlineData(60d)]
    [InlineData(45d)]
    [InlineData(-45d)]
    [InlineData(-60d)]
    public void AdoptedReference_NonBoundaryGripStillFollowsLanding(double landingDeg)
    {
        var landing = landingDeg * Math.PI / 180d;
        var resolved = TimberFramedBlockContentGripPresentationRules
            .ResolveFinalContentPresentation(
                currentWorldPresentationRadians: Math.PI / 2d,
                currentBlockRotationRadians: Math.PI,
                finalLandingPhysicalAngleRadians: landing,
                preserveAdoptedReferenceVerticalFamily: true);

        AssertAngleEqual(
            TimberFramedBlockContentReadableOrientationRules
                .Decide(landing)
                .PresentationAngle,
            resolved.FinalWorldPresentationAngle);
        Assert.False(
            TimberFramedBlockContentGripPresentationRules.PresentationPreserved(
                Math.PI / 2d,
                resolved.FinalWorldPresentationAngle));
    }

    private static void AssertAngleEqual(double expected, double actual)
    {
        var delta = TimberAnnotationReadabilityRules.NormalizeAngleDelta(
            actual - expected);
        Assert.True(Math.Abs(delta) <= 1e-10d, $"angle delta={delta:R}");
    }
}
