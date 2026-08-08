using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// CREATE 60° finalization + R3_RIGHT/LEFT content-variant contracts (A–K).
/// </summary>
public sealed class TimberFramedCombinedG5CreateFinalizeAndContentVariantTests
{
    public static TheoryData<double> CreateAnglesDeg { get; } = new()
    {
        0d,
        35d,
        -35d,
        90d,
    };

    [Theory]
    [MemberData(nameof(CreateAnglesDeg))]
    public void A_to_D_CreateFinalization_CorrectsEightyOneToSixty(double angleDeg)
    {
        var readable = angleDeg * Math.PI / 180d;
        var attachment = new TimberPlanarPoint(100d, 200d);
        var length = 18000d;
        var correct = TimberFramedCombinedG5CreateFirstSegmentRules.BuildCorrectedKnee(
            attachment,
            length,
            readable,
            sideSign: 1d);

        // Simulate AutoCAD dogleg/landing rewrite that left ~81° instead of 60°.
        var wrongAngle = 81d * Math.PI / 180d;
        var wrongKnee = new TimberPlanarPoint(
            attachment.X +
            (length * Math.Cos(wrongAngle) * Math.Cos(readable)) -
            (length * Math.Sin(wrongAngle) * Math.Sin(readable)),
            attachment.Y +
            (length * Math.Cos(wrongAngle) * Math.Sin(readable)) +
            (length * Math.Sin(wrongAngle) * Math.Cos(readable)));

        Assert.True(
            TimberFramedCombinedG5CreateFirstSegmentRules
                .TryResolveCreateFinalizationFromReadableAxis(
                    attachment,
                    wrongKnee,
                    readable,
                    sideSign: 1d,
                    out var corrected,
                    out var actualAngle,
                    out var changed));
        Assert.True(changed);
        Assert.True(
            TimberFramedCombinedG5CreateFirstSegmentRules.NeedsCreateFinalization(
                actualAngle));
        Assert.InRange(actualAngle, 80.9d, 81.1d);
        Assert.Equal(correct.X, corrected.X, 6);
        Assert.Equal(correct.Y, corrected.Y, 6);

        Assert.True(
            TimberFramedCombinedG5CreateFirstSegmentRules
                .TryMeasureFirstVisibleSegmentAngleDeg(
                    attachment.X,
                    attachment.Y,
                    corrected.X,
                    corrected.Y,
                    attachment.X - Math.Cos(readable) * 1000d,
                    attachment.Y - Math.Sin(readable) * 1000d,
                    attachment.X + Math.Cos(readable) * 1000d,
                    attachment.Y + Math.Sin(readable) * 1000d,
                    out var finalAngle));
        Assert.True(
            TimberFramedCombinedG5CreatePlacementRules.FirstSegmentAngleIsSixtyDegrees(
                finalAngle),
            $"final={finalAngle:R}");
    }

    [Fact]
    public void E_RightContent_DimsOnLandingBetweenKneeAndFrame()
    {
        var knee = new TimberPlanarPoint(0d, 0d);
        var item = new TimberPlanarPoint(100d, 0d);
        var dims = new TimberPlanarPoint(55d, 0d);
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.TryEvaluateLandingDimensionColumn(
                knee,
                item,
                dims,
                out var t,
                out var onLanding));
        Assert.True(onLanding);
        Assert.True(t > 0d && t < 1d);
        Assert.Equal(
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
            TimberFramedCombinedG5ContentVariantRules.FromWorldSide(
                TimberLeaderHorizontalSide.Right));
        Assert.Equal(
            "RIGHT",
            TimberFramedCombinedG5ContentVariantRules.ToContentVariantToken(
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide));
        Assert.True(
            TimberFramedBlockContentDefinitionRules.CalculateDimensionColumnLocalX(
                TimberFramedBlockContentKind.Circle,
                800d,
                2.5d,
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide) < 0d);
        Assert.True(
            TimberFramedBlockContentDefinitionRules.CalculateDimensionColumnLocalX(
                TimberFramedBlockContentKind.Circle,
                800d,
                2.5d,
                TimberFramedCombinedG5ContentVariantRules.LeftColumnSide) > 0d);
    }

    [Fact]
    public void F_LeftContent_DimsOnLandingBetweenKneeAndFrame()
    {
        var knee = new TimberPlanarPoint(0d, 0d);
        var item = new TimberPlanarPoint(100d, 0d);
        var dims = new TimberPlanarPoint(55d, 0d);
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.TryEvaluateLandingDimensionColumn(
                knee,
                item,
                dims,
                out var t,
                out var onLanding));
        Assert.True(onLanding);
        Assert.True(t > 0d && t < 1d);
        Assert.Equal(
            TimberFramedCombinedG5ContentVariantRules.LeftColumnSide,
            TimberFramedCombinedG5ContentVariantRules.FromWorldSide(
                TimberLeaderHorizontalSide.Left));
        Assert.Equal(
            TimberFramedBlockContentDimensionColumnSide.PositiveLocalX,
            TimberFramedCombinedG5ContentVariantRules.LeftColumnSide);
    }

    [Fact]
    public void G_H_GripSideCrossing_RequiresOppositeVariant()
    {
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.ShouldSwapContentVariant(
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
                TimberFramedCombinedG5ContentVariantRules.LeftColumnSide));
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.ShouldSwapContentVariant(
                TimberFramedCombinedG5ContentVariantRules.LeftColumnSide,
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide));
    }

    [Fact]
    public void I_GripWithoutSideCrossing_NoSwap()
    {
        Assert.False(
            TimberFramedCombinedG5ContentVariantRules.ShouldSwapContentVariant(
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide));
    }

    [Fact]
    public void J_CreateFinalization_IsCreateOnlyContract()
    {
        // Grip / refresh must not call first-segment finalization — guarded by
        // host source contracts; Core exposes the decision helper only.
        Assert.False(
            TimberFramedCombinedG5CreateFirstSegmentRules.NeedsCreateFinalization(60d));
        Assert.True(
            TimberFramedCombinedG5CreateFirstSegmentRules.NeedsCreateFinalization(81d));
    }

    [Fact]
    public void K_RefreshPlacement_PreservesGeometryContract()
    {
        Assert.False(
            TimberFramedCombinedG5RefreshPlacementRules.ManualOffsetMayMoveAnchor);
        Assert.True(
            TimberFramedCombinedG5RefreshPlacementRules.ShouldPreserveExistingPlacement(
                10d,
                20d,
                10d,
                20d,
                liveBlockScale: 50d,
                canonicalBlockScale: 50d));
    }

    [Fact]
    public void VariantKeys_RightAndLeftAreDistinctImmutable()
    {
        var right = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Circle,
            "MEDIUM",
            "Standard",
            "Standard",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined,
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide);
        var left = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Circle,
            "MEDIUM",
            "Standard",
            "Standard",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined,
            TimberFramedCombinedG5ContentVariantRules.LeftColumnSide);
        Assert.NotEqual(right, left);
        Assert.Contains("_RIGHT_", right, StringComparison.Ordinal);
        Assert.Contains("_LEFT_", left, StringComparison.Ordinal);
        Assert.True(
            TimberFramedBlockContentVariantRules.TryParseR3VariantKey(
                right,
                out var rightParse));
        Assert.True(rightParse.IsProductionCombinedTarget);
        Assert.Equal(
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
            rightParse.ContentVariantSide);
        Assert.True(
            TimberFramedBlockContentProductionGripNormalizeRules
                .IsProductionApplicableBlockContent(
                    TimberFramedBlockContentVariantRules.CreateSafeBlockName(right),
                    true,
                    true,
                    true));
        Assert.True(
            TimberFramedBlockContentProductionGripNormalizeRules
                .IsR3ContentVariantOnlyPath(
                    TimberFramedBlockContentVariantRules.CreateSafeBlockName(right)));
    }

    [Fact]
    public void SideDetection_UsesKneeFrameLanding_NotWorldXAlone()
    {
        // Vertical effective +X = (0,1): landing along +local X needs R3_RIGHT.
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.TryResolveRequiredContentVariant(
                kneeX: 0d,
                kneeY: 0d,
                frameCenterX: 0d,
                frameCenterY: 550d,
                effectiveLocalXAxisX: 0d,
                effectiveLocalXAxisY: 1d,
                out var required,
                out var contentLocalX,
                out _));
        Assert.True(contentLocalX > 0d);
        Assert.Equal(
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
            required);

        // Same frame with knee reflected through frame → R3_LEFT.
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.TryResolveRequiredContentVariant(
                kneeX: 0d,
                kneeY: 1100d,
                frameCenterX: 0d,
                frameCenterY: 550d,
                effectiveLocalXAxisX: 0d,
                effectiveLocalXAxisY: 1d,
                out var leftRequired,
                out var leftContentLocalX,
                out _));
        Assert.True(leftContentLocalX < 0d);
        Assert.Equal(
            TimberFramedCombinedG5ContentVariantRules.LeftColumnSide,
            leftRequired);

        // World L/R remains measurable for diagnostics but does not select layout.
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.TryMeasureWorldSide(
                attachmentX: 0d,
                attachmentY: 0d,
                contentCenterX: -100d,
                contentCenterY: 50d,
                startX: 0d,
                startY: 0d,
                endX: 0d,
                endY: 1000d,
                out var worldSide,
                out var signed));
        Assert.Equal(TimberLeaderHorizontalSide.Right, worldSide);
        Assert.True(signed > 0d);
    }

    [Theory]
    [MemberData(nameof(CreateAnglesDeg))]
    public void RightCreateFinalization_LandingStaysAlongReadable_NotSixtyTilt(
        double angleDeg)
    {
        var readable = angleDeg * Math.PI / 180d;
        var attachment = new TimberPlanarPoint(100d, 200d);
        var firstLength = 18000d;
        var landingLength = 550d;
        var wrongAngle = 81d * Math.PI / 180d;
        var wrongKnee = new TimberPlanarPoint(
            attachment.X +
            (firstLength * Math.Cos(wrongAngle) * Math.Cos(readable)) -
            (firstLength * Math.Sin(wrongAngle) * Math.Sin(readable)),
            attachment.Y +
            (firstLength * Math.Cos(wrongAngle) * Math.Sin(readable)) +
            (firstLength * Math.Sin(wrongAngle) * Math.Cos(readable)));
        // Stale content anchor from pre-finalization layout (old knee + landing).
        var staleLanding = new TimberPlanarPoint(
            wrongKnee.X + (landingLength * Math.Cos(readable)),
            wrongKnee.Y + (landingLength * Math.Sin(readable)));

        Assert.True(
            TimberFramedCombinedG5CreateFirstSegmentRules
                .TryResolveCreateFinalizationFromReadableAxis(
                    attachment,
                    wrongKnee,
                    readable,
                    sideSign: 1d,
                    out var correctedKnee,
                    out _,
                    out var changed));
        Assert.True(changed);

        var correctedLanding =
            TimberFramedCombinedG5CreateFirstSegmentRules.BuildCorrectedLandingEnd(
                correctedKnee,
                readable,
                landingLength);

        Assert.True(
            TimberFramedCombinedG5CreateFirstSegmentRules
                .TryMeasureLandingSegmentAngleToReadableDeg(
                    correctedKnee,
                    correctedLanding,
                    readable,
                    out var landingAngle));
        Assert.True(
            TimberFramedCombinedG5CreateFirstSegmentRules
                .LandingSegmentIsStraightAlongReadable(landingAngle),
            $"landingAngle={landingAngle:R}");

        // Bug regression: keeping stale BlockPosition tilts second segment.
        Assert.True(
            TimberFramedCombinedG5CreateFirstSegmentRules
                .TryMeasureLandingSegmentAngleToReadableDeg(
                    correctedKnee,
                    staleLanding,
                    readable,
                    out var staleAngle));
        Assert.False(
            TimberFramedCombinedG5CreateFirstSegmentRules
                .LandingSegmentIsStraightAlongReadable(staleAngle),
            $"staleAngle={staleAngle:R} must differ from straight landing");

        // First segment remains exactly 60°; second does not inherit that tilt.
        Assert.True(
            TimberFramedCombinedG5CreateFirstSegmentRules
                .TryMeasureFirstVisibleSegmentAngleDeg(
                    attachment.X,
                    attachment.Y,
                    correctedKnee.X,
                    correctedKnee.Y,
                    attachment.X - Math.Cos(readable) * 1000d,
                    attachment.Y - Math.Sin(readable) * 1000d,
                    attachment.X + Math.Cos(readable) * 1000d,
                    attachment.Y + Math.Sin(readable) * 1000d,
                    out var firstAngle));
        Assert.True(
            TimberFramedCombinedG5CreatePlacementRules.FirstSegmentAngleIsSixtyDegrees(
                firstAngle),
            $"first={firstAngle:R}");
        Assert.True(Math.Abs(landingAngle) < 1d);
        Assert.True(Math.Abs(firstAngle - 60d) < 1d);
    }

    [Fact]
    public void RightCreate_DimsOnLanding_LeftIsPositiveMirror()
    {
        // RIGHT create with PASS −local X: knee → dims → frame.
        var rightKnee = new TimberPlanarPoint(0d, 0d);
        var rightItem = new TimberPlanarPoint(550d, 0d);
        var rightDims = new TimberPlanarPoint(400d, 0d);
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.TryEvaluateLandingDimensionColumn(
                rightKnee,
                rightItem,
                rightDims,
                out var rightT,
                out var rightOnLanding));
        Assert.True(rightOnLanding);
        Assert.True(rightT > 0d && rightT < 1d);

        var rightLocalX =
            TimberFramedBlockContentDefinitionRules.CalculateDimensionColumnLocalX(
                TimberFramedBlockContentKind.Circle,
                frameWidthMm: 800d,
                dimensionPaperHeightMm: 2.5d,
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide);
        var leftLocalX =
            TimberFramedBlockContentDefinitionRules.CalculateDimensionColumnLocalX(
                TimberFramedBlockContentKind.Circle,
                frameWidthMm: 800d,
                dimensionPaperHeightMm: 2.5d,
                TimberFramedCombinedG5ContentVariantRules.LeftColumnSide);
        Assert.True(rightLocalX < 0d);
        Assert.True(leftLocalX > 0d);
        Assert.Equal(-rightLocalX, leftLocalX, 9);
    }

    [Fact]
    public void A_RightCreate_FinalGeometryRequiresRightContentVariant()
    {
        // Create landing along +local X (knee → frame): requires R3_RIGHT (−offset).
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.TryResolveRequiredContentVariant(
                kneeX: 0d,
                kneeY: 0d,
                frameCenterX: 550d,
                frameCenterY: 150d,
                effectiveLocalXAxisX: 1d,
                effectiveLocalXAxisY: 0d,
                out var required,
                out var contentLocalX,
                out _));
        Assert.True(contentLocalX > 0d);
        Assert.Equal(
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
            required);
        Assert.False(
            TimberFramedCombinedG5ContentVariantRules.ShouldSwapContentVariant(
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
                required));
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.ShouldSwapContentVariant(
                TimberFramedCombinedG5ContentVariantRules.LeftColumnSide,
                required),
            "CREATE that landed with LEFT BTR must swap to RIGHT from final knee/frame.");
    }

    [Fact]
    public void B_LeftCreate_FinalGeometryRequiresLeftContentVariant()
    {
        // Knee on +local X of frame (landing −local X): requires R3_LEFT (+offset).
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.TryResolveRequiredContentVariant(
                kneeX: 1100d,
                kneeY: 0d,
                frameCenterX: 550d,
                frameCenterY: 0d,
                effectiveLocalXAxisX: 1d,
                effectiveLocalXAxisY: 0d,
                out var required,
                out var contentLocalX,
                out _));
        Assert.True(contentLocalX < 0d);
        Assert.Equal(
            TimberFramedCombinedG5ContentVariantRules.LeftColumnSide,
            required);
        Assert.False(
            TimberFramedCombinedG5ContentVariantRules.ShouldSwapContentVariant(
                TimberFramedCombinedG5ContentVariantRules.LeftColumnSide,
                required));
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.ShouldSwapContentVariant(
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
                required),
            "Wrong RIGHT BTR with knee on +local X must swap to LEFT.");
    }

    [Fact]
    public void C_CreateFinalSideResolution_DoesNotAlterSixtyOrLandingMath()
    {
        // Content-variant ensure is a separate Core decision from 60° / landing
        // finalize helpers — side resolution must not call into first-segment math.
        var attachment = new TimberPlanarPoint(0d, 0d);
        var knee = TimberFramedCombinedG5CreateFirstSegmentRules.BuildCorrectedKnee(
            attachment,
            segmentLengthModelMm: 18000d,
            readableAngleRadians: 0d,
            sideSign: 1d);
        var landing =
            TimberFramedCombinedG5CreateFirstSegmentRules.BuildCorrectedLandingEnd(
                knee,
                readableAngleRadians: 0d,
                landingLengthModelMm: 550d);

        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.TryResolveRequiredContentVariant(
                knee.X,
                knee.Y,
                landing.X,
                landing.Y,
                effectiveLocalXAxisX: 1d,
                effectiveLocalXAxisY: 0d,
                out var required,
                out _,
                out _));
        Assert.Equal(
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
            required);

        Assert.True(
            TimberFramedCombinedG5CreateFirstSegmentRules
                .TryMeasureFirstVisibleSegmentAngleDeg(
                    attachment.X,
                    attachment.Y,
                    knee.X,
                    knee.Y,
                    -1000d,
                    0d,
                    1000d,
                    0d,
                    out var firstAngle));
        Assert.True(
            TimberFramedCombinedG5CreatePlacementRules.FirstSegmentAngleIsSixtyDegrees(
                firstAngle));
        Assert.True(
            TimberFramedCombinedG5CreateFirstSegmentRules
                .TryMeasureLandingSegmentAngleToReadableDeg(
                    knee,
                    landing,
                    readableAngleRadians: 0d,
                    out var landingAngle));
        Assert.True(
            TimberFramedCombinedG5CreateFirstSegmentRules
                .LandingSegmentIsStraightAlongReadable(landingAngle));
    }

    [Fact]
    public void D_ContentVariantDecision_IsIndependentOfMLeaderHandleIdentity()
    {
        // Swap changes BlockContentId / BTR only — Core side decision does not
        // invent a second entity. Handle identity is a host invariant guarded by
        // source contracts (same ObjectId before/after EnsureCorrect).
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.ShouldSwapContentVariant(
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
                TimberFramedCombinedG5ContentVariantRules.LeftColumnSide));
        Assert.False(
            TimberFramedCombinedG5ContentVariantRules.ShouldSwapContentVariant(
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide));
    }

    [Fact]
    public void E_GripCrossing_StillRequiresOppositeVariant()
    {
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.ShouldSwapContentVariant(
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
                TimberFramedCombinedG5ContentVariantRules.LeftColumnSide));
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.ShouldSwapContentVariant(
                TimberFramedCombinedG5ContentVariantRules.LeftColumnSide,
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide));
    }

    [Fact]
    public void F_GripWithoutSideCrossing_StillNoSwap()
    {
        Assert.False(
            TimberFramedCombinedG5ContentVariantRules.ShouldSwapContentVariant(
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide));
        Assert.False(
            TimberFramedCombinedG5ContentVariantRules.ShouldSwapContentVariant(
                TimberFramedCombinedG5ContentVariantRules.LeftColumnSide,
                TimberFramedCombinedG5ContentVariantRules.LeftColumnSide));
    }
}
