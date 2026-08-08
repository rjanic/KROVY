using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberAnnotationReadabilityRulesTests
{
    public static TheoryData<double, double, bool> FullLabelAndR3Cases { get; } =
        new()
        {
            { 0d, 0d, false },
            { 35d, 35d, false },
            { 80d, 80d, false },
            { 89d, 89d, false },
            { 90d, 90d, false },
            { 91d, -89d, true },
            { 100d, -80d, true },
            { 120d, -60d, true },
            { 135d, -45d, true },
            { 150d, -30d, true },
            { 179d, -1d, true },
            { 180d, 0d, true },
            { 181d, 1d, true },
            { 225d, 45d, true },
            { 269d, 89d, true },
            // WHITE DOBRÉ: 270° stays −90° (reads from left), not +90°.
            { 270d, -90d, false },
            { 271d, -89d, false },
            { 315d, -45d, false },
            { 359d, -1d, false },
            { 360d, 0d, false },
        };

    [Theory]
    [MemberData(nameof(FullLabelAndR3Cases))]
    public void NormalizeReadableRotation_MatchesSharedHalfPlaneContract(
        double rawDeg,
        double expectedReadableDeg,
        bool expectedFlip)
    {
        var raw = rawDeg * Math.PI / 180d;
        var readable =
            TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(raw);
        Assert.Equal(expectedReadableDeg * Math.PI / 180d, readable, 10);
        Assert.Equal(
            expectedReadableDeg,
            TimberAnnotationReadabilityRules.NormalizeReadableAngleDegrees(rawDeg),
            10);
        Assert.Equal(
            expectedReadableDeg * Math.PI / 180d,
            TimberAnnotationReadabilityRules.NormalizeReadableAngle(raw),
            10);
        Assert.Equal(
            expectedFlip,
            TimberAnnotationReadabilityRules.IsReadabilityFlipped(raw));
        Assert.True(
            readable >= -Math.PI / 2d - 1e-12d &&
            readable <= Math.PI / 2d + 1e-12d);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(35d)]
    [InlineData(80d)]
    [InlineData(89d)]
    [InlineData(91d)]
    [InlineData(120d)]
    [InlineData(150d)]
    [InlineData(179d)]
    [InlineData(180d)]
    [InlineData(181d)]
    [InlineData(225d)]
    [InlineData(269d)]
    [InlineData(271d)]
    [InlineData(315d)]
    [InlineData(359d)]
    public void ReverseStartEnd_SameReadableResult(double angleDeg)
    {
        var angle = angleDeg * Math.PI / 180d;
        var forward =
            TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(angle);
        var reverse =
            TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(
                angle + Math.PI);
        Assert.Equal(forward, reverse, 12);
    }

    [Theory]
    [InlineData(90d)]
    [InlineData(270d)]
    public void ReverseStartEnd_Verticals_AreDirectedOpposites(double angleDeg)
    {
        // WHITE DOBRÉ: 90° reads from the right, 270° from the left (−90°).
        // Reverse Start/End of a vertical swaps +90 ↔ −90 (directed opposites).
        var angle = angleDeg * Math.PI / 180d;
        var forward =
            TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(angle);
        var reverse =
            TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(
                angle + Math.PI);
        Assert.Equal(Math.PI / 2d, Math.Abs(forward), 10);
        Assert.Equal(Math.PI / 2d, Math.Abs(reverse), 10);
        Assert.Equal(Math.PI, Math.Abs(forward - reverse), 10);
    }

    [Fact]
    public void Exact270_DoesNotCollapseToPlus90()
    {
        var readable =
            TimberAnnotationReadabilityRules.NormalizeReadableAngleDegrees(270d);
        Assert.Equal(-90d, readable, 10);
        Assert.NotEqual(
            TimberAnnotationReadabilityRules.NormalizeReadableAngleDegrees(90d),
            readable,
            10);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NormalizeReadableRotation_RejectsNonFinite(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(value));
}

public sealed class TimberFramedBlockContentReadableOrientationRulesTests
{
    public static TheoryData<double> SourceAnglesDeg { get; } =
        new()
        {
            0d, 35d, 45d, 89d, 90d, 91d, 135d, 179d, 180d, 181d,
            225d, 269d, 270d, 271d, 315d, 359d,
        };

    /// <summary>
    /// WHITE DOBRÉ presentation table (screenshot authority).
    /// </summary>
    public static TheoryData<double, double, bool, bool> WhitePresentationTable
    {
        get;
    } =
        new()
        {
            // sourceDeg, presentationDeg, flip, incomingIsRight (R3_RIGHT)
            { 0d, 0d, false, true },
            { 35d, 35d, false, true },
            { 90d, -90d, true, false },
            { 135d, -45d, true, false },
            { 180d, 0d, true, false },
            { 225d, 45d, true, false },
            { 270d, -90d, false, true },
            { 315d, -45d, false, true },
        };

    [Theory]
    [MemberData(nameof(WhitePresentationTable))]
    public void WhiteScreenshot_PresentationTable(
        double sourceDeg,
        double expectedPresentationDeg,
        bool expectedFlip,
        bool incomingIsRight)
    {
        var decision =
            TimberFramedBlockContentReadableOrientationRules.Decide(
                sourceDeg * Math.PI / 180d);
        Assert.Equal(
            expectedPresentationDeg,
            decision.PresentationAngle * 180d / Math.PI,
            10);
        Assert.Equal(expectedFlip, decision.ReadableFlip);
        Assert.Equal(
            incomingIsRight
                ? TimberFramedCombinedG5ContentVariantRules.RightColumnSide
                : TimberFramedCombinedG5ContentVariantRules.LeftColumnSide,
            decision.IncomingLandingSide);
        Assert.True(
            TimberFramedBlockContentReadableOrientationRules
                .TryGetWhiteReferencePresentationDeg(
                    sourceDeg,
                    out var tableDeg,
                    out var tableFlip,
                    out var tableSide));
        Assert.Equal(expectedPresentationDeg, tableDeg, 10);
        Assert.Equal(expectedFlip, tableFlip);
        Assert.Equal(decision.IncomingLandingSide, tableSide);
    }

    [Theory]
    [MemberData(nameof(SourceAnglesDeg))]
    public void WorldSpace_CreateReadableTextAndTowardKnee(double angleDeg)
    {
        var angle = angleDeg * Math.PI / 180d;
        var startX = 0d;
        var startY = 0d;
        var endX = 1000d * Math.Cos(angle);
        var endY = 1000d * Math.Sin(angle);
        var physical =
            TimberFramedBlockContentReadableOrientationRules
                .SourcePhysicalAngleRadians(startX, startY, endX, endY);
        var decision =
            TimberFramedBlockContentReadableOrientationRules.Decide(physical);
        var snapshot =
            TimberFramedBlockContentReadableOrientationRules.Inspect(physical);

        Assert.Equal(
            decision.PresentationAngle * 180d / Math.PI,
            snapshot.PresentationAngleDeg,
            10);
        Assert.Equal(decision.ReadableFlip, snapshot.ReadableFlipApplied);
        Assert.Equal(decision.IncomingLandingSide, snapshot.IncomingLandingSide);
        Assert.True(
            TimberFramedBlockContentReadableOrientationRules
                .IsReadableTextAngleDegrees(snapshot.ItemTextWorldAngleDeg));
        Assert.True(
            TimberFramedBlockContentReadableOrientationRules.TextAnglesAreCoherent(
                snapshot.ItemTextWorldAngleDeg,
                snapshot.WidthTextWorldAngleDeg,
                snapshot.HeightTextWorldAngleDeg));
        Assert.Equal(
            snapshot.ReadableContentRotationDeg,
            snapshot.ItemTextWorldAngleDeg,
            10);

        var layout = TimberFramedCombinedG5CreatePlacementRules.CalculateCreate(
            attachmentX: (startX + endX) / 2d,
            attachmentY: (startY + endY) / 2d,
            rawElementAxisRadians: physical,
            contentKind: TimberFramedBlockContentKind.Circle,
            frameWidthMm: TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
            frameHeightMm: TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
            annotationScaleDenominator: 50,
            itemPaperHeightMm: 2.7d,
            dimensionPaperHeightMm: 2.5d,
            firstSegmentLengthModelMm:
                TimberItemLeaderLayoutCalculator.FirstSegmentLengthMm,
            landingLengthModelMm:
                TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm,
            dimensionColumnEnvelopeWidthMm: 100d,
            dimensionColumnSide: TimberFramedBlockContentDefinitionRules
                .DefaultCombinedDimensionColumnSide);

        Assert.Equal(
            decision.PresentationAngle,
            layout.ReadableAngleRadians,
            12);
        Assert.Equal(decision.ReadableFlip, layout.ReadabilityFlipped);

        Assert.True(
            TimberFramedBlockContentReadableOrientationRules
                .TryEvaluateCreateWorldDimensionsTowardKnee(
                    layout,
                    out var towardKneeDot,
                    out var worldKnee,
                    out var worldFrame,
                    out var worldDims));
        Assert.True(
            towardKneeDot > 0d,
            $"DimensionsTowardKneeDot={towardKneeDot} at {angleDeg}°");

        // Semantic order Knee→Dims→Frame on landing (0 < t < 1).
        Assert.True(
            TimberFramedCombinedG5ContentVariantRules.TryEvaluateLandingDimensionColumn(
                worldKnee,
                worldFrame,
                worldDims,
                out var parameterT,
                out var onLanding));
        Assert.True(onLanding, $"Dims not between knee and frame at {angleDeg}° t={parameterT}");
        Assert.True(parameterT > 0d && parameterT < 1d);

        Assert.Equal(layout.AttachmentLocal.X, (startX + endX) / 2d, 9);
        Assert.Equal(layout.AttachmentLocal.Y, (startY + endY) / 2d, 9);
        Assert.False(double.IsNaN(worldKnee.X));
        Assert.False(double.IsNaN(worldFrame.X));

        Assert.True(
            TimberFramedCombinedG5CreatePlacementRules.TryMeasureFirstSegmentAngleDeg(
                layout.AttachmentLocal.X,
                layout.AttachmentLocal.Y,
                worldKnee.X,
                worldKnee.Y,
                startX,
                startY,
                endX,
                endY,
                out var firstDeg));
        Assert.True(
            TimberFramedCombinedG5CreatePlacementRules.FirstSegmentAngleIsSixtyDegrees(
                firstDeg),
            $"Expected 60±0.01°, got {firstDeg} at {angleDeg}°");
    }

    [Theory]
    [MemberData(nameof(SourceAnglesDeg))]
    public void ReverseStartEnd_SamePresentationAndTowardKnee(double angleDeg)
    {
        var angle = angleDeg * Math.PI / 180d;
        var ax = 100d;
        var ay = 200d;
        var forwardPhysical = angle;
        var reversePhysical = angle + Math.PI;
        var forwardDecision =
            TimberFramedBlockContentReadableOrientationRules.Decide(forwardPhysical);
        var reverseDecision =
            TimberFramedBlockContentReadableOrientationRules.Decide(reversePhysical);
        var presentationDelta = Math.Abs(
            TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                forwardDecision.PresentationAngle - reverseDecision.PresentationAngle));
        // R3 construction-drawing boundary is deterministic: reverse
        // Start/End has the same visual presentation even at exact vertical.
        Assert.True(
            presentationDelta <= 1e-12d,
            $"Presentation forward={forwardDecision.PresentationAngle} reverse={reverseDecision.PresentationAngle}");

        TimberFramedBlockContentLayout Forward(
            double startX,
            double startY,
            double endX,
            double endY) =>
            TimberFramedCombinedG5CreatePlacementRules.CalculateCreate(
                ax,
                ay,
                Math.Atan2(endY - startY, endX - startX),
                TimberFramedBlockContentKind.Slot,
                TimberItemLeaderBlockDefinitionRules.SmallFrameWidthMm,
                TimberItemLeaderBlockDefinitionRules.FrameHeightMm,
                50,
                2.7d,
                2.5d,
                TimberItemLeaderLayoutCalculator.FirstSegmentLengthMm,
                TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm,
                100d,
                TimberFramedBlockContentDefinitionRules
                    .DefaultCombinedDimensionColumnSide);

        var a = Forward(
            ax - 500d * Math.Cos(angle),
            ay - 500d * Math.Sin(angle),
            ax + 500d * Math.Cos(angle),
            ay + 500d * Math.Sin(angle));
        var b = Forward(
            ax + 500d * Math.Cos(angle),
            ay + 500d * Math.Sin(angle),
            ax - 500d * Math.Cos(angle),
            ay - 500d * Math.Sin(angle));

        var readableDelta = Math.Abs(
            TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                a.ReadableAngleRadians - b.ReadableAngleRadians));
        Assert.True(readableDelta <= 1e-12d);
        Assert.True(
            TimberFramedBlockContentReadableOrientationRules
                .TryEvaluateCreateWorldDimensionsTowardKnee(
                    a, out var dotA, out _, out _, out _));
        Assert.True(
            TimberFramedBlockContentReadableOrientationRules
                .TryEvaluateCreateWorldDimensionsTowardKnee(
                    b, out var dotB, out _, out _, out _));
        Assert.True(dotA > 0d);
        Assert.True(dotB > 0d);
    }

    [Fact]
    public void Inspect_ExposesRequestedDiagnosticFields()
    {
        var snapshot =
            TimberFramedBlockContentReadableOrientationRules.Inspect(
                135d * Math.PI / 180d);
        Assert.Equal(135d, snapshot.SourcePhysicalAngleDeg, 10);
        Assert.Equal(135d, snapshot.RawContentRotationDeg, 10);
        Assert.Equal(-45d, snapshot.ReadableContentRotationDeg, 10);
        Assert.True(snapshot.ReadableFlipApplied);
        Assert.Equal(-45d, snapshot.ItemTextWorldAngleDeg, 10);
        Assert.Equal(-45d, snapshot.WidthTextWorldAngleDeg, 10);
        Assert.Equal(-45d, snapshot.HeightTextWorldAngleDeg, 10);
        Assert.Equal(-45d, snapshot.PresentationAngleDeg, 10);
        Assert.Equal(
            TimberFramedCombinedG5ContentVariantRules.LeftColumnSide,
            snapshot.IncomingLandingSide);
    }

    [Fact]
    public void Decide_Exact90And270_UseSameConstructionDrawingDirection()
    {
        var at90 =
            TimberFramedBlockContentReadableOrientationRules.Decide(
                Math.PI / 2d);
        var at270 =
            TimberFramedBlockContentReadableOrientationRules.Decide(
                3d * Math.PI / 2d);
        Assert.Equal(-90d, at90.PresentationAngle * 180d / Math.PI, 10);
        Assert.Equal(-90d, at270.PresentationAngle * 180d / Math.PI, 10);
        Assert.True(at90.ReadableFlip);
        Assert.False(at270.ReadableFlip);
    }

    [Theory]
    [InlineData(89d, 89d)]
    [InlineData(90d, -90d)]
    [InlineData(91d, -89d)]
    [InlineData(269d, 89d)]
    [InlineData(270d, -90d)]
    [InlineData(271d, -89d)]
    public void VerticalBoundary_UsesExplicitHostReferenceAndReverseIsIdentical(
        double sourceDeg,
        double expectedPresentationDeg)
    {
        var forward = TimberFramedBlockContentReadableOrientationRules.Decide(
            sourceDeg * Math.PI / 180d);
        var reverse = TimberFramedBlockContentReadableOrientationRules.Decide(
            (sourceDeg + 180d) * Math.PI / 180d);

        Assert.Equal(
            expectedPresentationDeg,
            forward.PresentationAngle * 180d / Math.PI,
            10);
        Assert.Equal(forward.PresentationAngle, reverse.PresentationAngle, 10);
        Assert.True(
            TimberFramedBlockContentReadableOrientationRules
                .IsReadableTextAngleDegrees(
                    forward.PresentationAngle * 180d / Math.PI));
    }

    [Theory]
    [InlineData(0d, 35d, -20d, false, false, 35d)]
    [InlineData(89d, 35d, -20d, false, false, 35d)]
    [InlineData(90d, -90d, 0d, true, true, 90d)]
    [InlineData(-90d, -90d, 0d, true, false, -90d)]
    [InlineData(180d, 0d, 0d, true, true, 180d)]
    public void CreateReferenceContentCorrection_UsesDirectedVerticalContract(
        double sourceDeg,
        double currentWorldDeg,
        double currentBlockDeg,
        bool expectedReferenceRule,
        bool expectedHalfTurn,
        double expectedOutputDeg)
    {
        var decision =
            TimberFramedBlockContentReadableOrientationRules
                .ResolveCreateReferenceFinalWorldPresentation(
                    sourceDeg * Math.PI / 180d,
                    currentWorldPresentationRadians:
                        currentWorldDeg * Math.PI / 180d,
                    currentBlockRotationRadians:
                        currentBlockDeg * Math.PI / 180d);
        Assert.Equal(expectedReferenceRule, decision.AppliesReferenceRule);
        Assert.Equal(expectedHalfTurn, decision.AppliesHalfTurn);
        Assert.Equal(
            expectedOutputDeg,
            decision.VerticalRuleOutput * 180d / Math.PI,
            10);
    }

    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(90d, 90d)]
    [InlineData(180d, 180d)]
    [InlineData(270d, -90d)]
    public void CreateReferenceContentCorrection_ChangesOnlyFinalContentWorldAngle(
        double sourceDeg,
        double expectedFinalContentDeg)
    {
        var source = sourceDeg * Math.PI / 180d;
        var basePresentation =
            TimberFramedBlockContentReadableOrientationRules
                .Decide(source)
                .PresentationAngle;
        var decision =
            TimberFramedBlockContentReadableOrientationRules
                .ResolveCreateReferenceFinalWorldPresentation(
                    source,
                    basePresentation,
                    currentBlockRotationRadians: 0d);
        var finalContent = decision.FinalWorldPresentation;

        Assert.Equal(
            expectedFinalContentDeg,
            finalContent * 180d / Math.PI,
            10);
    }

    [Theory]
    [InlineData(-90d, -90d, 0d, -90d, 0d, false)]
    [InlineData(90d, -90d, 0d, 90d, 180d, true)]
    [InlineData(180d, 0d, -42d, 180d, 138d, true)]
    public void CreateReferenceFinalWorldRegression_UsesRelativeBlockRotation(
        double sourceDeg,
        double currentWorldDeg,
        double currentBlockDeg,
        double expectedFinalWorldDeg,
        double expectedTargetBlockDeg,
        bool expectedHalfTurn)
    {
        var decision =
            TimberFramedBlockContentReadableOrientationRules
                .ResolveCreateReferenceFinalWorldPresentation(
                    sourceDeg * Math.PI / 180d,
                    currentWorldDeg * Math.PI / 180d,
                    currentBlockDeg * Math.PI / 180d);

        Assert.True(decision.AppliesReferenceRule);
        Assert.Equal(expectedHalfTurn, decision.AppliesHalfTurn);
        Assert.Equal(
            expectedFinalWorldDeg,
            decision.FinalWorldPresentation * 180d / Math.PI,
            10);
        Assert.Equal(
            expectedTargetBlockDeg,
            decision.TargetBlockRotation * 180d / Math.PI,
            10);
        Assert.Equal(
            expectedFinalWorldDeg,
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                    currentWorldDeg * Math.PI / 180d +
                    decision.BlockRotationCorrection) *
                180d /
                Math.PI,
            10);
    }

    [Theory]
    [InlineData(-90d, 0, true)]
    [InlineData(90d, 0, true)]
    [InlineData(180d, 0, true)]
    [InlineData(90d, 1, true)]
    [InlineData(180d, 1, true)]
    [InlineData(90d, 2, false)]
    [InlineData(180d, 2, false)]
    [InlineData(0d, 0, false)]
    [InlineData(270d, 0, true)]
    [InlineData(270d, 1, true)]
    [InlineData(270d, 2, false)]
    public void ExistingReferenceAdoptionRevision_MakesSecondRefreshIdempotent(
        double sourceDeg,
        int revision,
        bool expected)
    {
        Assert.Equal(
            expected,
            TimberFramedBlockContentReadableOrientationRules
                .ShouldAdoptReferencePresentation(
                    sourceDeg * Math.PI / 180d,
                    revision));
    }
}
