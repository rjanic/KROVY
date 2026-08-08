using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberFramedCombinedG5RefreshPlacementRulesTests
{
    public static TheoryData<double> RefreshAnglesDeg { get; } = new()
    {
        0d,
        35d,
        -35d,
        90d,
    };

    [Theory]
    [MemberData(nameof(RefreshAnglesDeg))]
    public void CreateThenRefresh_SameCanonicalInputs_AreGeometricallyIdempotent(
        double angleDeg)
    {
        var angle = angleDeg * Math.PI / 180d;
        var first = BuildLayout(angle, TimberLeaderHorizontalSide.Right);
        var refresh1 = BuildLayout(angle, TimberLeaderHorizontalSide.Right);
        var refresh2 = BuildLayout(angle, TimberLeaderHorizontalSide.Right);

        Assert.True(
            TimberFramedCombinedG5RefreshPlacementRules.LayoutsMatch(first, refresh1));
        Assert.True(
            TimberFramedCombinedG5RefreshPlacementRules.LayoutsMatch(refresh1, refresh2));
        Assert.True(
            TimberFramedCombinedG5RefreshPlacementRules.ShouldPreserveExistingPlacement(
                first.AttachmentLocal.X,
                first.AttachmentLocal.Y,
                refresh1.AttachmentLocal.X,
                refresh1.AttachmentLocal.Y,
                liveBlockScale: 50d,
                canonicalBlockScale: 50d));
    }

    [Theory]
    [MemberData(nameof(RefreshAnglesDeg))]
    public void LeftAndRight_CreateRefresh_StayIdempotentPerSide(double angleDeg)
    {
        var angle = angleDeg * Math.PI / 180d;
        foreach (var side in new[]
                 {
                     TimberLeaderHorizontalSide.Left,
                     TimberLeaderHorizontalSide.Right,
                 })
        {
            var created = BuildLayout(angle, side);
            var refreshed = BuildLayout(angle, side);
            Assert.True(
                TimberFramedCombinedG5RefreshPlacementRules.LayoutsMatch(
                    created,
                    refreshed));
        }
    }

    [Fact]
    public void ShouldPreserve_FailsWhenAttachmentMoved()
    {
        Assert.False(
            TimberFramedCombinedG5RefreshPlacementRules.ShouldPreserveExistingPlacement(
                liveAttachmentX: 0d,
                liveAttachmentY: 0d,
                canonicalAttachmentX: 10d,
                canonicalAttachmentY: 0d,
                liveBlockScale: 50d,
                canonicalBlockScale: 50d));
    }

    [Fact]
    public void ShouldPreserve_FailsWhenBlockScaleChanged()
    {
        Assert.False(
            TimberFramedCombinedG5RefreshPlacementRules.ShouldPreserveExistingPlacement(
                liveAttachmentX: 0d,
                liveAttachmentY: 0d,
                canonicalAttachmentX: 0d,
                canonicalAttachmentY: 0d,
                liveBlockScale: 50d,
                canonicalBlockScale: 100d));
    }

    [Fact]
    public void ManualOffset_MustNotMoveAnchor()
    {
        Assert.False(
            TimberFramedCombinedG5RefreshPlacementRules.ManualOffsetMayMoveAnchor);
    }

    [Fact]
    public void DefaultCreateSide_IsRight_AndNotReappliedByRefreshPreserve()
    {
        Assert.Equal(
            TimberLeaderHorizontalSide.Right,
            TimberFramedCombinedG5RefreshPlacementRules.DefaultCreateSide);

        var left = BuildLayout(0d, TimberLeaderHorizontalSide.Left);
        Assert.Equal(TimberLeaderHorizontalSide.Left, left.Side);
        Assert.True(
            TimberFramedCombinedG5RefreshPlacementRules.ShouldPreserveExistingPlacement(
                left.AttachmentLocal.X,
                left.AttachmentLocal.Y,
                left.AttachmentLocal.X,
                left.AttachmentLocal.Y,
                liveBlockScale: 50d,
                canonicalBlockScale: 50d));
    }

    [Fact]
    public void CreateAndGeometryRefresh_ShareOneCalculator()
    {
        var request = new TimberFramedBlockContentLayoutRequest(
            100d,
            200d,
            35d * Math.PI / 180d,
            TimberLeaderHorizontalSide.Left,
            TimberFramedBlockContentKind.Circle,
            350d,
            350d,
            50,
            3.5d,
            2.5d,
            350d,
            350d,
            400d,
            TimberFramedBlockContentPresentation.Combined,
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX);

        var create = TimberFramedBlockContentLayoutCalculator.Calculate(request);
        var refresh =
            TimberFramedCombinedG5RefreshPlacementRules.CalculateCanonical(
                request.AttachmentX,
                request.AttachmentY,
                request.ElementAxisRadians,
                request.Side,
                request.ContentKind,
                request.FrameWidthMm,
                request.FrameHeightMm,
                request.AnnotationScaleDenominator,
                request.ItemNumberPaperHeightMm,
                request.DimensionPaperHeightMm,
                request.FirstSegmentLengthModelMm,
                request.LandingLengthModelMm,
                request.DimensionColumnEnvelopeWidthMm,
                request.DimensionColumnSide);

        Assert.True(
            TimberFramedCombinedG5RefreshPlacementRules.LayoutsMatch(create, refresh));
    }

    [Theory]
    [InlineData(35d, 35d, 70d, 35d, 0d)]
    [InlineData(135d, -45d, -90d, -45d, 0d)]
    [InlineData(90d, 90d, 90d, 180d, 180d)]
    [InlineData(180d, 180d, 180d, 180d, 180d)]
    [InlineData(270d, -90d, -180d, -90d, 0d)]
    public void ContentOnlyRefresh_PreservesCreateWorldPresentationByRelativeDelta(
        double sourceDeg,
        double presentationBeforeDeg,
        double presentationAfterContentDeg,
        double blockAfterContentDeg,
        double expectedTargetBlockDeg)
    {
        var decision = ResolveRefresh(
            sourceDeg,
            sourceDeg,
            presentationBeforeDeg,
            presentationAfterContentDeg,
            blockAfterContentDeg);

        Assert.False(decision.SourceRotationChanged);
        AssertAngleDeg(presentationBeforeDeg, decision.PresentationAfterRefresh);
        AssertAngleDeg(0d, decision.PresentationRefreshDelta);
        AssertAngleDeg(expectedTargetBlockDeg, decision.TargetBlockRotation);
    }

    [Fact]
    public void KneeStretchThenRefresh_PreservesPostGripRelativeBlockRotation()
    {
        // CREATE base +35°, grip target BR −80° => final world −45°.
        var decision = ResolveRefresh(
            sourceBeforeDeg: 35d,
            sourceAfterDeg: 35d,
            presentationBeforeDeg: -45d,
            presentationAfterContentDeg: -45d,
            blockAfterContentDeg: -80d);

        AssertAngleDeg(-45d, decision.PresentationAfterRefresh);
        AssertAngleDeg(-80d, decision.TargetBlockRotation);
        AssertAngleDeg(0d, decision.PresentationRefreshDelta);
    }

    [Fact]
    public void SideCrossingBtrSwap_UsesMeasuredAfterStateAndRestoresWorldAngle()
    {
        // A BTR/AttrRef swap may temporarily change the measured axis by 180°.
        var decision = ResolveRefresh(
            sourceBeforeDeg: 135d,
            sourceAfterDeg: 135d,
            presentationBeforeDeg: -45d,
            presentationAfterContentDeg: 135d,
            blockAfterContentDeg: 100d);

        AssertAngleDeg(-45d, decision.PresentationAfterRefresh);
        AssertAngleDeg(0d, decision.PresentationRefreshDelta);
        AssertAngleDeg(-80d, decision.TargetBlockRotation);
    }

    [Fact]
    public void RefreshTwice_IsPresentationIdempotent()
    {
        var first = ResolveRefresh(35d, 35d, 35d, 70d, 35d);
        var second = ResolveRefresh(
            35d,
            35d,
            first.PresentationAfterRefresh * 180d / Math.PI,
            first.PresentationAfterRefresh * 180d / Math.PI,
            first.TargetBlockRotation * 180d / Math.PI);

        AssertAngleDeg(35d, first.PresentationAfterRefresh);
        AssertAngleDeg(35d, second.PresentationAfterRefresh);
        AssertAngleDeg(0d, first.PresentationRefreshDelta);
        AssertAngleDeg(0d, second.PresentationRefreshDelta);
        AssertAngleDeg(
            first.TargetBlockRotation * 180d / Math.PI,
            second.TargetBlockRotation);
    }

    [Fact]
    public void TrueSourceRotation_UsesSeparateLifecycleAndMayChangePresentation()
    {
        var decision = ResolveRefresh(
            sourceBeforeDeg: 35d,
            sourceAfterDeg: 90d,
            presentationBeforeDeg: 35d,
            presentationAfterContentDeg: 35d,
            blockAfterContentDeg: 0d);

        Assert.True(decision.SourceRotationChanged);
        AssertAngleDeg(-90d, decision.DesiredWorldPresentation);
        AssertAngleDeg(-90d, decision.PresentationAfterRefresh);
        Assert.False(Math.Abs(decision.PresentationRefreshDelta) <= 1e-12d);
    }

    private static R3RefreshPresentationDecision ResolveRefresh(
        double sourceBeforeDeg,
        double sourceAfterDeg,
        double presentationBeforeDeg,
        double presentationAfterContentDeg,
        double blockAfterContentDeg) =>
        TimberFramedCombinedG5RefreshPlacementRules
            .ResolveContentOnlyRefreshPresentation(
                sourceBeforeDeg * Math.PI / 180d,
                sourceAfterDeg * Math.PI / 180d,
                presentationBeforeDeg * Math.PI / 180d,
                presentationAfterContentDeg * Math.PI / 180d,
                blockAfterContentDeg * Math.PI / 180d);

    private static void AssertAngleDeg(double expectedDeg, double actualRadians)
    {
        var expected = expectedDeg * Math.PI / 180d;
        var delta = TimberAnnotationReadabilityRules.NormalizeAngleDelta(
            actualRadians - expected);
        Assert.True(Math.Abs(delta) <= 1e-10d, $"angle delta={delta:R}");
    }

    private static TimberFramedBlockContentLayout BuildLayout(
        double elementAxisRadians,
        TimberLeaderHorizontalSide side) =>
        TimberFramedCombinedG5RefreshPlacementRules.CalculateCanonical(
            attachmentX: 0d,
            attachmentY: 0d,
            elementAxisRadians,
            side,
            TimberFramedBlockContentKind.Circle,
            frameWidthMm: 350d,
            frameHeightMm: 350d,
            annotationScaleDenominator: 50,
            itemPaperHeightMm: 3.5d,
            dimensionPaperHeightMm: 2.5d,
            firstSegmentLengthModelMm:
                TimberItemLeaderLayoutCalculator.FirstSegmentLengthMm *
                TimberAnnotationScaleRules.GetScaleFactor(50),
            landingLengthModelMm:
                TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm *
                TimberAnnotationScaleRules.GetScaleFactor(50),
            dimensionColumnEnvelopeWidthMm:
                TimberFramedBlockContentDefinitionRules
                    .CalculateReferenceDimensionEnvelopeWidthMm(2.5d) *
                TimberAnnotationScaleRules.GetScaleFactor(50),
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX);
}
